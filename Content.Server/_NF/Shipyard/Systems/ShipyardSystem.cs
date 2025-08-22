using Content.Server.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Content.Shared._NF.CCVar;
using Robust.Shared.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Content.Server._NF.Station.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Content.Server.Shuttles.Save;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;
using System;
using Robust.Shared.Log; // ADDED: For Logger

// using Content.Shared._NF.Shipyard.Systems; // REMOVED: Not needed, SharedShipyardSystem is in Content.Shared._NF.Shipyard
using Content.Shared.Shuttles.Save; // For RequestLoadShipMessage, ShipConvertedToSecureFormatMessage
using Content.Shared.Access.Components; // For IdCardComponent
using Robust.Shared.Map.Components; // For MapGridComponent
using Content.Server._NF.StationEvents.Components; // For LinkedLifecycleGridParentComponent
using Content.Server.Maps; // For GameMapPrototype
using Content.Shared.Chat; // For InGameICChatType
using Content.Shared.Radio; // For RadioChannelPrototype
using Robust.Shared.Prototypes; // For Loc
using Content.Server.Radio.EntitySystems; // For RadioSystem
using Content.Server._NF.Shuttles.Systems; // For ShuttleRecordsSystem
using Content.Shared.Shuttles.Components; // For IFFComponent
using Content.Shared.Popups; // For PopupSystem
using Robust.Shared.Audio.Systems; // For SharedAudioSystem
using Content.Server.Administration.Logs; // For IAdminLogManager
using Content.Shared.Database; // For LogType
using Content.Server.Administration.Commands; // For ShipBlacklistService

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem : SharedShipyardSystem
{
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private readonly IServerNetManager _netManager = default!; // Ensure this is present

    public MapId? ShipyardMap { get; private set; }
    private float _shuttleIndex;
    private const float ShuttleSpawnBuffer = 1f;
    private ISawmill _sawmill = default!;
    private bool _enabled;
    private float _baseSaleRate;
    private readonly HashSet<string> _loadedShipIds = new();
    private readonly HashSet<NetUserId> _currentlyLoading = new();

    // The type of error from the attempted sale of a ship.
    public enum ShipyardSaleError
    {
        Success, // Ship can be sold.
        Undocked, // Ship is not docked with the station.
        OrganicsAboard, // Sapient intelligence is aboard, cannot sell, would delete the organics
        InvalidShip, // Ship is invalid
        MessageOverwritten, // Overwritten message.
    }

    // TODO: swap to strictly being a formatted message.
    public struct ShipyardSaleResult
    {
        public ShipyardSaleError Error; // Whether or not the ship can be sold.
        public string? OrganicName; // In case an organic is aboard, this will be set to the first that's aboard.
        public string? OverwrittenMessage; // The message to write if Error is MessageOverwritten.
    }

    public override void Initialize()
    {
        base.Initialize();

        // FIXME: Load-bearing jank - game doesn't want to create a shipyard map at this point.
        _enabled = _configManager.GetCVar(NFCCVars.Shipyard);
        _configManager.OnValueChanged(NFCCVars.Shipyard, SetShipyardEnabled); // NOTE: run immediately set to false, see comment above

        _configManager.OnValueChanged(NFCCVars.ShipyardSellRate, SetShipyardSellRate, true);
    _sawmill = Logger.GetSawmill("shipyard");
    SubscribeNetworkEvent<RequestLoadShipMessage>(HandleLoadShipRequest);

    SubscribeLocalEvent<ShipyardConsoleComponent, ComponentStartup>(OnShipyardStartup);
    SubscribeLocalEvent<ShipyardConsoleComponent, BoundUIOpenedEvent>(OnConsoleUIOpened);
    SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleSellMessage>(OnSellMessage);
    SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleSaveMessage>(OnSaveMessage);
    SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsolePurchaseMessage>(OnPurchaseMessage);
    SubscribeLocalEvent<ShipyardConsoleComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
    SubscribeLocalEvent<ShipyardConsoleComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);
    SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart); 
    SubscribeLocalEvent<StationDeedSpawnerComponent, MapInitEvent>(OnInitDeedSpawner);

    }
    private void HandleLoadShipRequest(RequestLoadShipMessage message, EntitySessionEventArgs args)
    {
        var playerSession = args.SenderSession;
        if (playerSession == null)
            return;

        // Prevent loading while already loading
        if (_currentlyLoading.Contains(playerSession.UserId))
        {
            _sawmill.Warning($"Player {playerSession.Name} attempted to load ship while already loading");
            return;
        }
        _currentlyLoading.Add(playerSession.UserId);

        _sawmill.Info($"SHIP LOAD REQUEST: Player {playerSession.Name} ({playerSession.UserId}) attempting to load ship");
        var shipSerializationSystem = _entitySystemManager.GetEntitySystem<ShipSerializationSystem>();

        try
        {
            // Attempting to deserialize YAML
            var shipGridData = shipSerializationSystem.DeserializeShipGridDataFromYaml(message.YamlData, playerSession.UserId, out bool wasLegacyConverted);
            
            // Check if ship is blacklisted BEFORE loading
            if (ShipBlacklistService.IsBlacklisted(shipGridData.Metadata.Checksum))
            {
                var reason = ShipBlacklistService.GetBlacklistReason(shipGridData.Metadata.Checksum) ?? "Blacklisted by admin";
                _sawmill.Warning($"SECURITY: Blocked blacklisted ship load attempt by {playerSession.Name} - {reason}");
                
                var playerEntity = playerSession.AttachedEntity;
                if (playerEntity.HasValue)
                {
                    _popup.PopupEntity($"This ship has been blacklisted: {reason}", 
                                     playerEntity.Value, playerEntity.Value, PopupType.LargeCaution);
                }
                return;
            }
            
            // Log this attempt for admin convenience
            var attemptId = ShipBlacklistService.LogShipAttempt(shipGridData.Metadata.Checksum, playerSession.Name, shipGridData.Metadata.ShipName + "_" + shipGridData.Metadata.Timestamp.ToString("yyyyMMdd_HHmmss") + ".yml");

            // Duplicate ship detection using original grid ID
            // Checking for duplicate ship
            if (_loadedShipIds.Contains(shipGridData.Metadata.OriginalGridId))
            {
                _sawmill.Warning($"SECURITY: Duplicate ship load attempt - ID {shipGridData.Metadata.OriginalGridId} by {playerSession.Name}");
                
                // Show in-character message for duplicate ship loading
                var playerEntity = playerSession.AttachedEntity;
                if (playerEntity.HasValue)
                {
                    _popup.PopupEntity("This ship has already been loaded this round.", 
                                     playerEntity.Value, playerEntity.Value, PopupType.LargeCaution);
                }
                return;
            }

            // Find a shipyard console with an ID card inserted
            var consoles = EntityQueryEnumerator<ShipyardConsoleComponent>();
            EntityUid? targetConsole = null;
            EntityUid? idCardInConsole = null;
            
            while (consoles.MoveNext(out var consoleUid, out var console))
            {
                // Check if this console has an ID card inserted (like in OnPurchaseMessage)
                if (console.TargetIdSlot.ContainerSlot?.ContainedEntity is { Valid: true } targetId)
                {
                    if (HasComp<IdCardComponent>(targetId))
                    {
                        // Check if ID card already has a deed
                        if (HasComp<ShuttleDeedComponent>(targetId))
                        {
                            _sawmill.Warning($"SECURITY: Player {playerSession.Name} attempted to load ship on card {targetId} that already has a deed");
                            return;
                        }
                        
                        targetConsole = consoleUid;
                        idCardInConsole = targetId;
                        break;
                    }
                }
            }

            if (!targetConsole.HasValue || !idCardInConsole.HasValue)
            {
                _sawmill.Warning($"SECURITY: Player {playerSession.Name} attempted ship load without valid ID card in console");
                return;
            }

            if (!TryComp<TransformComponent>(targetConsole.Value, out var consoleXform) || consoleXform.GridUid == null)
            {
                _sawmill.Error($"Shipyard console transform invalid for ship loading.");
                return;
            }

            // Reconstruct ship on shipyard map (similar to TryAddShuttle behavior)
            SetupShipyardIfNeeded();
            if (ShipyardMap == null)
            {
                _sawmill.Error($"Shipyard map not available for ship loading.");
                return;
            }

            var newShipGridUid = shipSerializationSystem.ReconstructShipOnMap(shipGridData, ShipyardMap.Value, new Vector2(500f + _shuttleIndex, 1f));
            // Track this ship as loaded to prevent duplicate loading
            _loadedShipIds.Add(shipGridData.Metadata.OriginalGridId);
            
            _sawmill.Info($"SHIP LOADED: {shipGridData.Metadata.ShipName} by {playerSession.Name} ({playerSession.UserId})");

            // Update shuttle index for spacing
            if (TryComp<MapGridComponent>(newShipGridUid, out var gridComp))
            {
                _shuttleIndex += gridComp.LocalAABB.Width + ShuttleSpawnBuffer;
            }

            // Ensure the loaded ship has a ShuttleComponent (required for docking and IFF)
            if (!HasComp<ShuttleComponent>(newShipGridUid))
            {
                var shuttleComp = EnsureComp<ShuttleComponent>(newShipGridUid);
                // Added ShuttleComponent
            }

            // Add IFFComponent to make it show up properly on radar as a friendly player ship
            if (!HasComp<IFFComponent>(newShipGridUid))
            {
                var iffComp = EnsureComp<IFFComponent>(newShipGridUid);
                _shuttle.AddIFFFlag(newShipGridUid, IFFFlags.IsPlayerShuttle);
                _shuttle.SetIFFColor(newShipGridUid, IFFComponent.IFFColor);
                // Added IFFComponent
            }

            var shipName = shipGridData.Metadata.ShipName;
            string finalShipName = shipName;

            // Set up station for the loaded ship exactly like purchased ships
            EntityUid? shuttleStation = null;
            if (_prototypeManager.TryIndex<GameMapPrototype>(shipName, out var stationProto))
            {
                List<EntityUid> gridUids = new()
                {
                    newShipGridUid
                };
                shuttleStation = _station.InitializeNewStation(stationProto.Stations[shipName], gridUids);
                finalShipName = Name(shuttleStation.Value); // Use station name with prefix like purchased ships
                // Created station from prototype

                var vesselInfo = EnsureComp<ExtraShuttleInformationComponent>(shuttleStation.Value);
                vesselInfo.Vessel = shipName;
            }
            else
            {
                // No station prototype found
            }

            // Dock the loaded ship to the console's grid (similar to purchase behavior)
            if (TryComp<ShuttleComponent>(newShipGridUid, out var shuttleComponent))
            {
                var targetGrid = consoleXform.GridUid.Value;
                _shuttle.TryFTLDock(newShipGridUid, shuttleComponent, targetGrid);
                // Attempted to dock ship
            }

            // Add deed to the ID card in the console - mark as loaded to prevent exploits
            var deedComponent = EnsureComp<ShuttleDeedComponent>(idCardInConsole.Value);
            deedComponent.ShuttleUid = GetNetEntity(newShipGridUid);
            TryParseShuttleName(deedComponent, finalShipName);
            deedComponent.ShuttleOwner = playerSession.Name;
            deedComponent.PurchasedWithVoucher = true; // Mark as loaded
            _sawmill.Info($"Added deed to ID card in console {idCardInConsole.Value}");

            // Also add deed to the ship itself (like purchased ships) but mark as loaded (not purchasable)
            var shipDeedComponent = EnsureComp<ShuttleDeedComponent>(newShipGridUid);
            shipDeedComponent.ShuttleUid = GetNetEntity(newShipGridUid);
            TryParseShuttleName(shipDeedComponent, finalShipName);
            shipDeedComponent.ShuttleOwner = playerSession.Name;
            shipDeedComponent.PurchasedWithVoucher = true; // Mark as loaded to prevent sale

            // Station information already set up above during station creation

            // Send radio announcement like purchased ships do
            if (TryComp<ShipyardConsoleComponent>(targetConsole.Value, out var consoleComponent))
            {
                var playerEntity = playerSession.AttachedEntity ?? EntityUid.Invalid;
                SendLoadMessage(targetConsole.Value, playerEntity, finalShipName, consoleComponent.ShipyardChannel);
                if (consoleComponent.SecretShipyardChannel is { } secretChannel)
                    SendLoadMessage(targetConsole.Value, playerEntity, finalShipName, secretChannel, secret: true);
                // Sent radio announcements
            }

            // Play success sound like purchase confirmation 
            if (TryComp<ShipyardConsoleComponent>(targetConsole.Value, out var loadConsoleComponent))
            {
                var playerEntity = playerSession.AttachedEntity ?? EntityUid.Invalid;
                if (playerEntity != EntityUid.Invalid)
                {
                    // Use the same confirm sound as purchases - _audio is in Consoles partial class
                    _audio.PlayEntity(loadConsoleComponent.ConfirmSound, playerEntity, targetConsole.Value);
                }
            }
            
            // Admin log for ship loading
            _adminLogger.Add(LogType.ShipYardUsage, LogImpact.Medium, $"{playerSession.Name} loaded ship {finalShipName} (Original ID: {shipGridData.Metadata.OriginalGridId}) via {ToPrettyString(targetConsole.Value)}");
            
            // Handle legacy ship conversion - force update the file to new secure format
            if (wasLegacyConverted)
            {
                _sawmill.Info($"SECURITY: Converting legacy SHA ship to secure format for {playerSession.Name}");
                
                var convertedYaml = shipSerializationSystem.GetConvertedLegacyShipYaml(shipGridData, playerSession.Name, message.YamlData);
                if (!string.IsNullOrEmpty(convertedYaml))
                {
                    // Send the converted YAML back to client to overwrite their file
                    var conversionMessage = new ShipConvertedToSecureFormatMessage
                    {
                        ConvertedYamlData = convertedYaml,
                        ShipName = shipGridData.Metadata.ShipName
                    };
                    
                    RaiseNetworkEvent(conversionMessage, playerSession);
                    // Sent converted ship file
                    
                    // Admin log for security audit trail
                    _adminLogger.Add(LogType.ShipYardUsage, LogImpact.High, $"Legacy SHA ship '{finalShipName}' automatically converted to secure format for player {playerSession.Name}");
                }
                else
                {
                    _sawmill.Error($"Failed to generate converted YAML for legacy ship - player {playerSession.Name} should manually re-save their ship");
                }
            }
            
            // Ship loading completed
        }
        catch (InvalidOperationException e)
        {
            _sawmill.Error($"SECURITY VIOLATION: Ship load failed for {playerSession.Name} - tampering detected: {e.Message}");
            
            // Show clear message for any InvalidOperationException (including checksum failures)
            var playerEntity = playerSession.AttachedEntity;
            if (playerEntity.HasValue)
            {
                if (e.Message.Contains("Checksum mismatch"))
                {
                    _popup.PopupEntity("Ship data has been tampered with. Loading failed.", 
                                     playerEntity.Value, playerEntity.Value, PopupType.LargeCaution);
                }
                else
                {
                    _popup.PopupEntity($"Ship loading failed: {e.Message}", 
                                     playerEntity.Value, playerEntity.Value, PopupType.LargeCaution);
                }
            }
        }
        catch (UnauthorizedAccessException e)
        {
            _sawmill.Error($"SECURITY: Unauthorized ship load attempt by {playerSession.Name}: {e.Message}");
            
            // Try to extract ship name from YAML for logging (basic parsing)
            var shipDetails = "Unknown Ship";
            try
            {
                if (message.YamlData.Contains("shipName:"))
                {
                    var lines = message.YamlData.Split('\n');
                    var shipNameLine = lines.FirstOrDefault(l => l.Trim().StartsWith("shipName:"));
                    if (shipNameLine != null)
                    {
                        var shipName = shipNameLine.Split(':')[1]?.Trim() ?? "Unknown";
                        shipDetails = $"'{shipName}'";
                    }
                }
            }
            catch
            {
                // Ignore parsing errors, use default
            }
            
            // Show in-character tamper detection message and play warning sound
            var playerEntity = playerSession.AttachedEntity;
            if (playerEntity.HasValue)
            {
                if (e.Message.Contains("checksum validation failed") || e.Message.Contains("tampered"))
                {
                    _popup.PopupEntity("SECURITY ALERT: Ship data integrity compromised! Tampering detected.", 
                                     playerEntity.Value, playerEntity.Value, PopupType.LargeCaution);
                    
                    // Play warning buzz sound
                    _audio.PlayPvs("/Audio/Machines/buzz_sigh.ogg", playerEntity.Value);
                    
                    // Log security violation and send admin alert with ship details
                    _adminLogger.Add(LogType.ShipYardUsage, LogImpact.High, 
                        $"SECURITY VIOLATION: Ship checksum tampering detected - Player {playerSession.Name} ({playerSession.UserId}) attempted to load tampered ship data for ship {shipDetails}");
                    
                    // Send alert to online admins via chat with ship details
                    _chatManager.SendAdminAlert($"SHIP TAMPERING: {playerSession.Name} attempted to load ship {shipDetails} with invalid checksum (tampering detected)");
                }
                else
                {
                    _popup.PopupEntity($"Ship loading failed: {e.Message}", 
                                     playerEntity.Value, playerEntity.Value, PopupType.LargeCaution);
                }
            }
        }
        catch (Exception e)
        {
            _sawmill.Error($"An unexpected error occurred during ship loading for {playerSession.Name}: {e.Message}");
            
            // Try to extract ship name from YAML for logging (basic parsing)
            var shipDetails = "Unknown Ship";
            try
            {
                if (message.YamlData.Contains("shipName:"))
                {
                    var lines = message.YamlData.Split('\n');
                    var shipNameLine = lines.FirstOrDefault(l => l.Trim().StartsWith("shipName:"));
                    if (shipNameLine != null)
                    {
                        var shipName = shipNameLine.Split(':')[1]?.Trim() ?? "Unknown";
                        shipDetails = $"'{shipName}'";
                    }
                }
            }
            catch
            {
                // Ignore parsing errors, use default
            }
            
            // Show player feedback for any ship loading failure
            var playerEntity = playerSession.AttachedEntity;
            if (playerEntity.HasValue)
            {
                if (e.Message.Contains("Alias") && e.Message.Contains("anchor"))
                {
                    // YAML corruption - potential tampering
                    _popup.PopupEntity("Ship data appears corrupted or tampered with. Loading failed.", 
                                     playerEntity.Value, playerEntity.Value, PopupType.LargeCaution);
                    
                    // Play warning sound
                    _audio.PlayPvs("/Audio/Machines/buzz_sigh.ogg", playerEntity.Value);
                    
                    // Log potential tampering and send admin alert
                    _adminLogger.Add(LogType.ShipYardUsage, LogImpact.Medium, 
                        $"SHIP CORRUPTION: Player {playerSession.Name} ({playerSession.UserId}) attempted to load corrupted/tampered ship data for ship {shipDetails} - YAML parsing failed: {e.Message}");
                    
                    // Send alert to online admins
                    _chatManager.SendAdminAlert($"SHIP CORRUPTION: {playerSession.Name} attempted to load ship {shipDetails} with corrupted/tampered YAML data");
                }
                else
                {
                    _popup.PopupEntity($"Ship loading failed: {e.Message}", 
                                     playerEntity.Value, playerEntity.Value, PopupType.Large);
                }
            }
        }
        finally
        {
            // Always remove player from loading set when done (success or failure)
            _currentlyLoading.Remove(playerSession.UserId);
        }
    }

    public override void Shutdown()
    {
        _configManager.UnsubValueChanged(NFCCVars.Shipyard, SetShipyardEnabled);
        _configManager.UnsubValueChanged(NFCCVars.ShipyardSellRate, SetShipyardSellRate);
    }
    private void OnShipyardStartup(EntityUid uid, ShipyardConsoleComponent component, ComponentStartup args)
    {
        if (!_enabled)
            return;
        InitializeConsole();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
        {
            CleanupShipyard();
        } 

    private void SetShipyardEnabled(bool value)
    {
        if (_enabled == value)
            return;

        _enabled = value;

        if (value)
            SetupShipyardIfNeeded();
        else
            CleanupShipyard();
    }

    private void SetShipyardSellRate(float value)
    {
        _baseSaleRate = Math.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>
    /// Adds a ship to the shipyard, calculates its price, and attempts to ftl-dock it to the given station
    /// </summary>
    /// <param name="stationUid">The ID of the station to dock the shuttle to</param>
    /// <param name="shuttlePath">The path to the shuttle file to load. Must be a grid file!</param>
    /// <param name="shuttleEntityUid">The EntityUid of the shuttle that was purchased</param>
    /// <summary>
    /// Purchases a shuttle and docks it to the grid the console is on, independent of station data.
    /// </summary>
    /// <param name="consoleUid">The entity of the shipyard console to dock to its grid</param>
    /// <param name="shuttlePath">The path to the shuttle file to load. Must be a grid file!</param>
    /// <param name="shuttleEntityUid">The EntityUid of the shuttle that was purchased</param>
    public bool TryPurchaseShuttle(EntityUid consoleUid, ResPath shuttlePath, [NotNullWhen(true)] out EntityUid? shuttleEntityUid)
    {
        // Get the grid the console is on
        if (!TryComp<TransformComponent>(consoleUid, out var consoleXform)
            || consoleXform.GridUid == null
            || !TryAddShuttle(shuttlePath, out var shuttleGrid)
            || !TryComp<ShuttleComponent>(shuttleGrid, out var shuttleComponent))
        {
            shuttleEntityUid = null;
            return false;
        }

        var price = _pricing.AppraiseGrid(shuttleGrid.Value, null);
        var targetGrid = consoleXform.GridUid.Value;

        _sawmill.Info($"Shuttle {shuttlePath} was purchased at {ToPrettyString(consoleUid)} for {price:f2}");
        _shuttle.TryFTLDock(shuttleGrid.Value, shuttleComponent, targetGrid);
        shuttleEntityUid = shuttleGrid;
        return true;
    }

    /// <summary>
    /// Loads a shuttle into the ShipyardMap from a file path
    /// </summary>
    /// <param name="shuttlePath">The path to the grid file to load. Must be a grid file!</param>
    /// <returns>Returns the EntityUid of the shuttle</returns>
    private bool TryAddShuttle(ResPath shuttlePath, [NotNullWhen(true)] out EntityUid? shuttleGrid)
    {
        shuttleGrid = null;
        SetupShipyardIfNeeded();
        if (ShipyardMap == null)
            return false;

        if (!_mapLoader.TryLoadGrid(ShipyardMap.Value, shuttlePath, out var grid, offset: new Vector2(500f + _shuttleIndex, 1f)))
        {
            _sawmill.Error($"Unable to spawn shuttle {shuttlePath}");
            return false;
        }

        _shuttleIndex += grid.Value.Comp.LocalAABB.Width + ShuttleSpawnBuffer;

        shuttleGrid = grid.Value.Owner;
        return true;
    }

    /// <summary>
    /// Checks a shuttle to make sure that it is docked to the given station, and that there are no lifeforms aboard. Then it teleports tagged items on top of the console, appraises the grid, outputs to the server log, and deletes the grid
    /// </summary>
    /// <param name="stationUid">The ID of the station that the shuttle is docked to</param>
    /// <param name="shuttleUid">The grid ID of the shuttle to be appraised and sold</param>
    /// <param name="consoleUid">The ID of the console being used to sell the ship</param>
    /// <summary>
    /// Sells a shuttle, checking that it is docked to the grid the console is on, and not to a station.
    /// </summary>
    /// <param name="shuttleUid">The grid ID of the shuttle to be appraised and sold</param>
    /// <param name="consoleUid">The ID of the console being used to sell the ship</param>
    /// <param name="bill">The amount the shuttle is sold for</param>
    public ShipyardSaleResult TrySellShuttle(EntityUid shuttleUid, EntityUid consoleUid, out int bill)
    {
        ShipyardSaleResult result = new ShipyardSaleResult();
        bill = 0;

        if (!HasComp<ShuttleComponent>(shuttleUid)
            || !TryComp(shuttleUid, out TransformComponent? xform)
            || !TryComp<TransformComponent>(consoleUid, out var consoleXform)
            || consoleXform.GridUid == null
            || ShipyardMap == null)
        {
            result.Error = ShipyardSaleError.InvalidShip;
            return result;
        }

        var targetGrid = consoleXform.GridUid.Value;
        var gridDocks = _docking.GetDocks(targetGrid);
        var shuttleDocks = _docking.GetDocks(shuttleUid);
        var isDocked = false;

        foreach (var shuttleDock in shuttleDocks)
        {
            foreach (var gridDock in gridDocks)
            {
                if (shuttleDock.Comp.DockedWith == gridDock.Owner)
                {
                    isDocked = true;
                    break;
                }
            }
            if (isDocked)
                break;
        }

        if (!isDocked)
        {
            _sawmill.Warning($"shuttle is not docked to the console's grid");
            result.Error = ShipyardSaleError.Undocked;
            return result;
        }

        var mobQuery = GetEntityQuery<MobStateComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        var charName = FoundOrganics(shuttleUid, mobQuery, xformQuery);
        if (charName is not null)
        {
            _sawmill.Warning($"organics on board");
            result.Error = ShipyardSaleError.OrganicsAboard;
            result.OrganicName = charName;
            return result;
        }

        if (TryComp<ShipyardConsoleComponent>(consoleUid, out var comp))
        {
            CleanGrid(shuttleUid, consoleUid);
        }

        bill = (int)_pricing.AppraiseGrid(shuttleUid, LacksPreserveOnSaleComp);
        QueueDel(shuttleUid);
        _sawmill.Info($"Sold shuttle {shuttleUid} for {bill}");

        // Update all record UI (skip records, no new records)
        _shuttleRecordsSystem.RefreshStateForAll(true);

        result.Error = ShipyardSaleError.Success;
        return result;
    }

    private void CleanGrid(EntityUid grid, EntityUid destination)
    {
        var xform = Transform(grid);
        var enumerator = xform.ChildEnumerator;
        var entitiesToPreserve = new List<EntityUid>();

        while (enumerator.MoveNext(out var child))
        {
            FindEntitiesToPreserve(child, ref entitiesToPreserve);
        }
        foreach (var ent in entitiesToPreserve)
        {
            // Teleport this item and all its children to the floor (or space).
            _transform.SetCoordinates(ent, new EntityCoordinates(destination, 0, 0));
            _transform.AttachToGridOrMap(ent);
        }
    }

    // checks if something has the ShipyardPreserveOnSaleComponent and if it does, adds it to the list
    private void FindEntitiesToPreserve(EntityUid entity, ref List<EntityUid> output)
    {
        if (TryComp<ShipyardSellConditionComponent>(entity, out var comp) && comp.PreserveOnSale == true)
        {
            output.Add(entity);
            return;
        }
        else if (TryComp<ContainerManagerComponent>(entity, out var containers))
        {
            foreach (var container in containers.Containers.Values)
            {
                foreach (var ent in container.ContainedEntities)
                {
                    FindEntitiesToPreserve(ent, ref output);
                }
            }
        }
    }

    // returns false if it has ShipyardPreserveOnSaleComponent, true otherwise
    private bool LacksPreserveOnSaleComp(EntityUid uid)
    {
        return !TryComp<ShipyardSellConditionComponent>(uid, out var comp) || comp.PreserveOnSale == false;
    }
    private void CleanupShipyard()
    {
        if (ShipyardMap == null || !_map.MapExists(ShipyardMap.Value))
        {
            ShipyardMap = null;
            return;
        }

        _map.DeleteMap(ShipyardMap.Value);
    }

    public void SetupShipyardIfNeeded()
    {
        if (ShipyardMap != null && _map.MapExists(ShipyardMap.Value))
            return;

        _map.CreateMap(out var shipyardMap);
        ShipyardMap = shipyardMap;

        _map.SetPaused(ShipyardMap.Value, false);
    }

    // <summary>
    // Tries to rename a shuttle deed and update the respective components.
    // Returns true if successful.
    //
    // Null name parts are promptly ignored.
    // </summary>
    public bool TryRenameShuttle(EntityUid uid, ShuttleDeedComponent? shuttleDeed, string? newName, string? newSuffix)
    {
        if (!Resolve(uid, ref shuttleDeed))
            return false;

        var shuttle = shuttleDeed.ShuttleUid;
        if (shuttle != null
             && TryGetEntity(shuttle.Value, out var shuttleEntity)
             && _station.GetOwningStation(shuttleEntity.Value) is { Valid: true } shuttleStation)
        {
            shuttleDeed.ShuttleName = newName;
            shuttleDeed.ShuttleNameSuffix = newSuffix;
            Dirty(uid, shuttleDeed);

            var fullName = GetFullName(shuttleDeed);
            _station.RenameStation(shuttleStation, fullName, loud: false);
            _metaData.SetEntityName(shuttleEntity.Value, fullName);
            _metaData.SetEntityName(shuttleStation, fullName);
        }
        else
        {
            _sawmill.Error($"Could not rename shuttle {ToPrettyString(shuttle):entity} to {newName}");
            return false;
        }

        //TODO: move this to an event that others hook into.
        if (shuttleDeed.ShuttleUid != null &&
            _shuttleRecordsSystem.TryGetRecord(shuttleDeed.ShuttleUid.Value, out var record))
        {
            record.Name = newName ?? "";
            record.Suffix = newSuffix ?? "";
            _shuttleRecordsSystem.TryUpdateRecord(record);
        }

        return true;
    }

    /// <summary>
    /// Returns the full name of the shuttle component in the form of [prefix] [name] [suffix].
    /// </summary>
    public static string GetFullName(ShuttleDeedComponent comp)
    {
        string?[] parts = { comp.ShuttleName, comp.ShuttleNameSuffix };
        return string.Join(' ', parts.Where(it => it != null));
    }

    private void SendLoadMessage(EntityUid uid, EntityUid player, string name, string shipyardChannel, bool secret = false)
    {
        var channel = _prototypeManager.Index<RadioChannelPrototype>(shipyardChannel);

        if (secret)
        {
            _radio.SendRadioMessage(uid, Loc.GetString("shipyard-console-docking-secret"), channel, uid);
        }
        else
        {
            _radio.SendRadioMessage(uid, Loc.GetString("shipyard-console-docking", ("owner", player), ("vessel", name)), channel, uid);
        }
    }
}
