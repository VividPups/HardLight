using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server._NF.RoundNotifications.Events;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Server.CrewManifest;
using Content.Server._NF.ShuttleRecords.Components;
using Content.Server._NF.ShuttleRecords;
using Content.Server._HL.RoundPersistence.Components;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Content.Shared._NF.ShuttleRecords.Components;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared.Shuttles.Components;
using RobustTimer = Robust.Shared.Timing.Timer;
using Content.Shared.StationRecords;
using Content.Shared.CrewManifest;
using Content.Shared._HL.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;
using System.Numerics;

namespace Content.Server._HL.RoundPersistence.Systems;

/// <summary>
/// System that handles saving and restoring critical game data across round restarts.
/// This ensures that ships remain functional, expeditions continue working, and player
/// records are preserved when the primary station is deleted and recreated.
/// </summary>
public sealed class RoundPersistenceSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;
    [Dependency] private readonly CrewManifestSystem _crewManifest = default!;
    [Dependency] private readonly ShuttleRecordsSystem _shuttleRecords = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly MetaDataSystem _metaDataSystem = default!;
    [Dependency] private readonly SalvageSystem _salvageSystem = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// Entity that persists across rounds to store our data
    /// </summary>
    private EntityUid? _persistentEntity;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("round-persistence");

        // Listen for round events
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);

        // Listen for station creation to restore data
        SubscribeLocalEvent<StationDataComponent, ComponentInit>(OnStationCreated);

        // Listen for ship spawning to restore IFF and associations
        SubscribeLocalEvent<ShuttleComponent, ComponentInit>(OnShuttleCreated);

        // Listen for expedition console map initialization to handle shuttle consoles
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, MapInitEvent>(OnExpeditionConsoleMapInit);

        // Monitor expedition data changes
        SubscribeLocalEvent<SalvageExpeditionDataComponent, ComponentShutdown>(OnExpeditionDataRemoved);

        // Monitor shuttle records changes
        SubscribeLocalEvent<ShuttleRecordsConsoleComponent, ComponentShutdown>(OnShuttleRecordsRemoved);

        // Monitor station records changes
        SubscribeLocalEvent<StationRecordsComponent, ComponentShutdown>(OnStationRecordsRemoved);

        // Set up periodic UI updates for expeditions to ensure timers work correctly
        RobustTimer.SpawnRepeating(TimeSpan.FromSeconds(1), () =>
        {
            if (!_cfg.GetCVar(HLCCVars.RoundPersistenceEnabled) || !_cfg.GetCVar(HLCCVars.RoundPersistenceExpeditions))
                return;

            UpdateExpeditionUIs();
        }, System.Threading.CancellationToken.None);

        _sawmill.Info("Round persistence system initialized");
    }

    /// <summary>
    /// Called when round restart cleanup begins - save all critical data
    /// </summary>
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        if (!_cfg.GetCVar(HLCCVars.RoundPersistenceEnabled))
            return;

        _sawmill.Info("Round restart detected, saving persistent data...");

        EnsurePersistentEntity();
        SaveAllCriticalData();
    }

    /// <summary>
    /// Called when a new round starts - restore data to new stations
    /// </summary>
    private void OnRoundStarted(RoundStartedEvent ev)
    {
        if (!_cfg.GetCVar(HLCCVars.RoundPersistenceEnabled))
            return;

        _sawmill.Info("Round started, will restore data when stations are created");
    }

    /// <summary>
    /// Called when a new station is created - restore relevant data
    /// </summary>
    private void OnStationCreated(EntityUid uid, StationDataComponent component, ComponentInit args)
    {
        // Small delay to ensure the station is fully initialized
        RobustTimer.Spawn(TimeSpan.FromSeconds(1), () => RestoreStationData(uid, component));
    }

    /// <summary>
    /// Called when a shuttle is created - restore IFF and association data
    /// </summary>
    private void OnShuttleCreated(EntityUid uid, ShuttleComponent component, ComponentInit args)
    {
        RobustTimer.Spawn(TimeSpan.FromSeconds(0.5f), () => RestoreShuttleData(uid, component));
    }

    /// <summary>
    /// HARDLIGHT: Completely overhauled to work with independent console data instead of station data
    /// Called when an expedition console is map initialized - restore console data from persistence
    /// </summary>
    private void OnExpeditionConsoleMapInit(EntityUid uid, SalvageExpeditionConsoleComponent component, MapInitEvent args)
    {
        if (!_cfg.GetCVar(HLCCVars.RoundPersistenceEnabled) || !_cfg.GetCVar(HLCCVars.RoundPersistenceExpeditions))
            return;

        // HARDLIGHT: Use a delay to ensure the console is fully initialized
        RobustTimer.Spawn(TimeSpan.FromMilliseconds(100), () =>
        {
            RestoreConsoleExpeditionData(uid, component);
        });
    }    /// <summary>
    /// HARDLIGHT: Restore expedition data directly to console from persistence storage
    /// </summary>
    private void RestoreConsoleExpeditionData(EntityUid consoleUid, SalvageExpeditionConsoleComponent consoleComp)
    {
        // Get the grid this console is on to identify the shuttle
        if (!TryComp<TransformComponent>(consoleUid, out var xform) || xform.GridUid == null)
        {
            Log.Warning($"Console {ToPrettyString(consoleUid)} has no grid - cannot restore expedition data");
            return;
        }

        var gridUid = xform.GridUid.Value;
        var gridName = MetaData(gridUid).EntityName;

        // Look for stored expedition data for this console/grid
        var persistenceQuery = AllEntityQuery<RoundPersistenceComponent>();
        while (persistenceQuery.MoveNext(out var persistenceUid, out var persistence))
        {
            // Check stored console data for this grid/shuttle
            if (persistence.ConsoleData.TryGetValue(gridName, out var storedConsoleData))
            {
                Log.Info($"Restoring expedition data for console {ToPrettyString(consoleUid)} on grid {gridName}");

                // Restore the local expedition data directly
                if (consoleComp.LocalExpeditionData == null)
                {
                    consoleComp.LocalExpeditionData = new SalvageExpeditionDataComponent();
                }

                var localData = consoleComp.LocalExpeditionData;

                // Clear existing missions and restore from persistence
                localData.Missions.Clear();
                foreach (var (index, persistedMission) in storedConsoleData.Missions)
                {
                    localData.Missions[index] = new SalvageMissionParams
                    {
                        Index = persistedMission.Index,
                        Seed = persistedMission.Seed,
                        Difficulty = persistedMission.Difficulty,
                        MissionType = (SalvageMissionType)persistedMission.MissionType
                    };
                }

                // Restore other properties
                localData.ActiveMission = storedConsoleData.ActiveMission;
                localData.NextIndex = storedConsoleData.NextIndex;
                localData.Cooldown = storedConsoleData.Cooldown;
                localData.NextOffer = storedConsoleData.NextOffer;
                localData.CanFinish = storedConsoleData.CanFinish;
                localData.CooldownTime = storedConsoleData.CooldownTime;

                // Update the console UI by raising the dirty event
                Dirty(consoleUid, consoleComp);

                Log.Info($"Successfully restored {storedConsoleData.Missions.Count} missions for console on {gridName}");
                return;
            }
        }

        Log.Debug($"No stored expedition data found for console {ToPrettyString(consoleUid)} on grid {gridName}");
    }

    /// <summary>
    /// Ensure we have a persistent entity to store data
    /// </summary>
    private void EnsurePersistentEntity()
    {
        if (_persistentEntity == null || !EntityManager.EntityExists(_persistentEntity.Value))
        {
            // Create a persistent entity on a dedicated map that won't be cleaned up
            var mapId = _mapManager.CreateMap();
            _persistentEntity = EntityManager.SpawnEntity(null, new MapCoordinates(Vector2.Zero, mapId));
            EntityManager.EnsureComponent<RoundPersistenceComponent>(_persistentEntity.Value);

            var metaData = EntityManager.EnsureComponent<MetaDataComponent>(_persistentEntity.Value);
            _metaDataSystem.SetEntityName(_persistentEntity.Value, "Round Persistence Entity", metaData);

            _sawmill.Info($"Created persistent entity {_persistentEntity.Value} on map {mapId}");
        }
    }

    /// <summary>
    /// Save all critical data from all stations
    /// </summary>
    private void SaveAllCriticalData()
    {
        if (_persistentEntity == null)
            return;

        if (!TryComp<RoundPersistenceComponent>(_persistentEntity.Value, out var persistence))
            return;

        // Clear old data
        persistence.ExpeditionData.Clear();
        persistence.ShuttleRecords.Clear();
        persistence.StationRecords.Clear();
        persistence.ShipData.Clear();
        persistence.PlayerPayments.Clear();
        persistence.ConsoleData.Clear();

        // Save current round info
        persistence.RoundNumber = _gameTicker.RoundId;
        persistence.LastSaveTime = _timing.CurTime;

        // Save data from all stations
        var stationQuery = EntityQueryEnumerator<StationDataComponent>();
        while (stationQuery.MoveNext(out var stationUid, out var stationData))
        {
            var stationName = MetaData(stationUid).EntityName;
            SaveStationData(stationUid, stationData, stationName, persistence);
        }

        // Save console expedition data (independent of stations)
        if (_cfg.GetCVar(HLCCVars.RoundPersistenceExpeditions))
        {
            SaveConsoleData(persistence);
        }

        // Save ship data from all shuttles
        var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, IFFComponent>();
        while (shuttleQuery.MoveNext(out var shuttleUid, out var shuttle, out var iff))
        {
            SaveShuttleData(shuttleUid, shuttle, iff, persistence);
        }

        _sawmill.Info($"Saved persistent data for {persistence.ExpeditionData.Count} stations, {persistence.ConsoleData.Count} consoles, and {persistence.ShipData.Count} ships");
    }

    /// <summary>
    /// Save data from a specific station
    /// </summary>
    private void SaveStationData(EntityUid stationUid, StationDataComponent stationData, string stationName, RoundPersistenceComponent persistence)
    {
        // Save expedition data
        if (_cfg.GetCVar(HLCCVars.RoundPersistenceExpeditions) && TryComp<SalvageExpeditionDataComponent>(stationUid, out var expeditionData))
        {
            var persistedMissions = new Dictionary<ushort, PersistedMissionParams>();
            foreach (var (index, mission) in expeditionData.Missions)
            {
                persistedMissions[index] = new PersistedMissionParams
                {
                    Index = mission.Index,
                    Seed = mission.Seed,
                    Difficulty = mission.Difficulty,
                    MissionType = (int)mission.MissionType
                };
            }

            persistence.ExpeditionData[stationName] = new PersistedExpeditionData
            {
                Missions = persistedMissions,
                ActiveMission = expeditionData.ActiveMission,
                NextIndex = expeditionData.NextIndex,
                Cooldown = expeditionData.Cooldown,
                NextOffer = expeditionData.NextOffer,
                CanFinish = expeditionData.CanFinish,
                CooldownTime = expeditionData.CooldownTime
            };
        }

        // Save shuttle records data
        if (_cfg.GetCVar(HLCCVars.RoundPersistenceShuttleRecords))
        {
            var shuttleRecordsQuery = AllEntityQuery<ShuttleRecordsConsoleComponent>();
            var allShuttleRecords = new List<ShuttleRecord>();
            while (shuttleRecordsQuery.MoveNext(out var consoleUid, out var consoleComp))
            {
                var consoleStation = _station.GetOwningStation(consoleUid);
                if (consoleStation == stationUid)
                {
                    var records = _shuttleRecords.GetAllShuttleRecords();
                    allShuttleRecords.AddRange(records);
                    break;
                }
            }

            if (allShuttleRecords.Count > 0)
            {
                persistence.ShuttleRecords[stationName] = allShuttleRecords;
            }
        }

        // Save station records and crew manifest
        if (_cfg.GetCVar(HLCCVars.RoundPersistenceStationRecords) && TryComp<StationRecordsComponent>(stationUid, out var stationRecords))
        {
            var allRecords = _stationRecords.GetRecordsOfType<GeneralStationRecord>(stationUid).ToList();
            var persistedRecords = new PersistedStationRecords
            {
                StationName = stationName,
                GeneralRecords = new Dictionary<uint, GeneralStationRecord>(),
                NextRecordId = (uint)(allRecords.Count > 0 ? allRecords.Max(r => r.Item1) + 1 : 1)
            };

            // Copy general records
            foreach (var (id, record) in allRecords)
            {
                persistedRecords.GeneralRecords[id] = record;
            }

            // Get crew manifest
            var (_, manifestEntries) = _crewManifest.GetCrewManifest(stationUid);
            if (manifestEntries != null)
            {
                persistedRecords.CrewManifest = manifestEntries.Entries.ToList();
            }

            persistence.StationRecords[stationName] = persistedRecords;
        }
    }

    /// <summary>
    /// Save data from a specific shuttle
    /// </summary>
    private void SaveShuttleData(EntityUid shuttleUid, ShuttleComponent shuttle, IFFComponent iff, RoundPersistenceComponent persistence)
    {
        if (!_cfg.GetCVar(HLCCVars.RoundPersistenceShipData))
            return;

        var netEntity = GetNetEntity(shuttleUid);
        var shipName = MetaData(shuttleUid).EntityName;

        // Try to get ownership information
        string ownerName = "Unknown";
        string ownerUserId = "Unknown";
        string stationAssociation = "Unknown";

        // Look for shuttle deed component to get owner info
        if (TryComp<Content.Shared._NF.Shipyard.Components.ShuttleDeedComponent>(shuttleUid, out var deed))
        {
            ownerName = deed.ShuttleOwner ?? "Unknown";
            shipName = deed.ShuttleName ?? shipName;
        }

        // Try to determine station association
        var owningStation = _station.GetOwningStation(shuttleUid);
        if (owningStation != null)
        {
            stationAssociation = MetaData(owningStation.Value).EntityName;
        }

        var transform = Transform(shuttleUid);
        var position = transform.WorldPosition;

        persistence.ShipData[netEntity] = new PersistedShipData
        {
            ShipName = shipName,
            OwnerName = ownerName,
            OwnerUserId = ownerUserId,
            StationAssociation = stationAssociation,
            IFFFlags = IFFFlags.None, // TODO: Access through ShuttleSystem when available
            IFFColor = IFFComponent.IFFColor,  // Use default color for now
            LastKnownPosition = position,
            LastSeenTime = DateTime.UtcNow,
            ShipClass = "Player Ship" // Default for now
        };
    }

    /// <summary>
    /// Save expedition data from all salvage consoles
    /// </summary>
    private void SaveConsoleData(RoundPersistenceComponent persistence)
    {
        var consoleQuery = EntityQueryEnumerator<SalvageExpeditionConsoleComponent>();
        while (consoleQuery.MoveNext(out var consoleUid, out var consoleComp))
        {
            if (consoleComp.LocalExpeditionData == null)
                continue;

            // Get the grid name as identifier
            var gridUid = Transform(consoleUid).GridUid;
            if (gridUid == null)
                continue;

            var gridName = MetaData(gridUid.Value).EntityName;
            if (string.IsNullOrEmpty(gridName))
                continue;

            var expeditionData = consoleComp.LocalExpeditionData;
            var persistedMissions = new Dictionary<ushort, PersistedMissionParams>();

            foreach (var (index, mission) in expeditionData.Missions)
            {
                persistedMissions[index] = new PersistedMissionParams
                {
                    Index = mission.Index,
                    Seed = mission.Seed,
                    Difficulty = mission.Difficulty,
                    MissionType = (int)mission.MissionType
                };
            }

            persistence.ConsoleData[gridName] = new StoredConsoleData
            {
                Missions = persistedMissions,
                ActiveMission = expeditionData.ActiveMission,
                NextIndex = expeditionData.NextIndex,
                Cooldown = expeditionData.Cooldown,
                NextOffer = expeditionData.NextOffer,
                CanFinish = expeditionData.CanFinish,
                CooldownTime = expeditionData.CooldownTime
            };
        }
    }

    /// <summary>
    /// Restore data to a newly created station
    /// </summary>
    private void RestoreStationData(EntityUid stationUid, StationDataComponent stationData)
    {
        if (_persistentEntity == null || !TryComp<RoundPersistenceComponent>(_persistentEntity.Value, out var persistence))
            return;

        var stationName = MetaData(stationUid).EntityName;
        _sawmill.Info($"Restoring data for station: {stationName}");

        // Restore expedition data
        if (persistence.ExpeditionData.TryGetValue(stationName, out var expeditionData))
        {
            var expeditionComp = EnsureComp<SalvageExpeditionDataComponent>(stationUid);
            expeditionComp.Missions.Clear();
            foreach (var (key, persistedMission) in expeditionData.Missions)
            {
                var mission = new SalvageMissionParams
                {
                    Index = persistedMission.Index,
                    Seed = persistedMission.Seed,
                    Difficulty = persistedMission.Difficulty,
                    MissionType = (SalvageMissionType)persistedMission.MissionType
                };
                expeditionComp.Missions[key] = mission;
            }
            expeditionComp.ActiveMission = expeditionData.ActiveMission;
            expeditionComp.NextIndex = expeditionData.NextIndex;
            expeditionComp.Cooldown = expeditionData.Cooldown;
            expeditionComp.CanFinish = expeditionData.CanFinish;
            expeditionComp.CooldownTime = expeditionData.CooldownTime;

            // Important: Recalculate timing relative to current round time
            var currentTime = _timing.CurTime;
            var timeSinceLastSave = currentTime - expeditionData.NextOffer;

            if (timeSinceLastSave.TotalSeconds > 0)
            {
                // Timer expired during restart - missions should be available now
                expeditionComp.NextOffer = currentTime;
                expeditionComp.Cooldown = false;

                // Clear missions if active mission is invalid or finished
                if (expeditionComp.ActiveMission != 0 && !expeditionComp.Missions.ContainsKey(expeditionComp.ActiveMission))
                {
                    expeditionComp.ActiveMission = 0;
                    expeditionComp.CanFinish = false;
                    // Properly regenerate missions using the salvage system's GenerateMissions method
                    _salvageSystem.ForceGenerateMissions(expeditionComp);
                }

                _sawmill.Info($"Expedition timer expired during restart, missions will be regenerated automatically");
            }
            else
            {
                // Timer hasn't expired yet - preserve original timing
                expeditionComp.NextOffer = expeditionData.NextOffer;
                _sawmill.Info($"Expedition timer preserved, {(expeditionData.NextOffer - currentTime).TotalSeconds:F1} seconds remaining");
            }

            // Mark component as dirty to trigger UI updates
            Dirty(stationUid, expeditionComp);

            // Use a slight delay to ensure the station is fully initialized, then force console updates
            RobustTimer.Spawn(TimeSpan.FromMilliseconds(500), () =>
            {
                // Update expedition consoles directly since UpdateConsoles is private
                var consoleQuery = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent, TransformComponent>();
                while (consoleQuery.MoveNext(out var consoleUid, out var consoleComp, out var uiComp, out var xform))
                {
                    var consoleStation = _station.GetOwningStation(consoleUid, xform);
                    if (consoleStation == stationUid)
                    {
                        // Force UI update by triggering console update logic
                        if (TryComp<SalvageExpeditionDataComponent>(stationUid, out var stationData))
                        {
                            var state = GetExpeditionState((stationUid, stationData));
                            _ui.SetUiState((consoleUid, uiComp), SalvageConsoleUiKey.Expedition, state);
                        }
                    }
                }
                _sawmill.Info($"Updated expedition console UIs for station {stationName}");
            });

            _sawmill.Info($"Restored expedition data with {expeditionData.Missions.Count} missions, NextOffer: {expeditionComp.NextOffer}, Claimed: {expeditionComp.Claimed}");
        }

        // Restore shuttle records
        if (persistence.ShuttleRecords.TryGetValue(stationName, out var shuttleRecords))
        {
            // Find all shuttle records consoles for this station and update them
            var shuttleRecordsQuery = AllEntityQuery<ShuttleRecordsConsoleComponent>();
            while (shuttleRecordsQuery.MoveNext(out var consoleUid, out var consoleComp))
            {
                var consoleStation = _station.GetOwningStation(consoleUid);
                if (consoleStation == stationUid)
                {
                    _shuttleRecords.RestoreShuttleRecords(shuttleRecords);
                    _sawmill.Info($"Restored {shuttleRecords.Count} shuttle records");
                    break;
                }
            }
        }

        // Restore shuttle-to-station assignments
        // This is critical for salvage missions to work properly
        RestoreShuttleStationAssignments(stationUid, stationName, persistence);

        // Restore station records
        if (persistence.StationRecords.TryGetValue(stationName, out var stationRecordsData))
        {
            var stationRecordsComp = EnsureComp<StationRecordsComponent>(stationUid);

            // Restore general records
            foreach (var (id, record) in stationRecordsData.GeneralRecords)
            {
                var key = new StationRecordKey(id, stationUid);
                _stationRecords.AddRecordEntry(key, record);
            }

            _sawmill.Info($"Restored {stationRecordsData.GeneralRecords.Count} station records");
        }
    }

    /// <summary>
    /// Restore data to a newly created shuttle
    /// </summary>
    private void RestoreShuttleData(EntityUid shuttleUid, ShuttleComponent shuttle)
    {
        if (_persistentEntity == null || !TryComp<RoundPersistenceComponent>(_persistentEntity.Value, out var persistence))
            return;

        var netEntity = GetNetEntity(shuttleUid);

        if (persistence.ShipData.TryGetValue(netEntity, out var shipData))
        {
            // TODO: Restore IFF data - requires ShuttleSystem access
            // var iffComp = EnsureComp<IFFComponent>(shuttleUid);
            // iffComp.Flags = shipData.IFFFlags;  // Access violation - need ShuttleSystem
            // iffComp.Color = shipData.IFFColor;  // Access violation - need ShuttleSystem

            // Update metadata
            _metaDataSystem.SetEntityName(shuttleUid, shipData.ShipName);

            _sawmill.Info($"Restored metadata for ship: {shipData.ShipName}");
        }
    }

    /// <summary>
    /// Ensure shuttles are properly assigned to the new station after round restart.
    /// This fixes the "no owning station found" error for salvage missions.
    /// </summary>
    private void RestoreShuttleStationAssignments(EntityUid stationUid, string stationName, RoundPersistenceComponent persistence)
    {
        // Find all shuttles with expedition consoles that should belong to this station
        var shuttleConsoleQuery = AllEntityQuery<SalvageExpeditionConsoleComponent, TransformComponent>();
        var shuttlesAssigned = 0;

        while (shuttleConsoleQuery.MoveNext(out var consoleUid, out _, out var xform))
        {
            // Get the grid this console is on
            var shuttleGrid = xform.GridUid;
            if (shuttleGrid == null || !TryComp<ShuttleComponent>(shuttleGrid, out var shuttleComp))
                continue;

            // Check if this shuttle has expedition data that matches this station
            var currentStation = _station.GetOwningStation(shuttleGrid.Value);

            // Only assign shuttles that are truly unowned or have malformed station data to avoid stealing from other stations
            if (currentStation == null || !EntityManager.EntityExists(currentStation.Value))
            {
                // Add the shuttle grid to this station using the proper StationSystem method
                _station.AddGridToStation(stationUid, shuttleGrid.Value);
                shuttlesAssigned++;

                _sawmill.Info($"Assigned shuttle grid {shuttleGrid} (with expedition console {consoleUid}) to station {stationName}");
            }
        }

        // Also check for any shuttle grids that have saved ship data but no station assignment
        foreach (var (netEntity, shipData) in persistence.ShipData)
        {
            if (TryGetEntity(netEntity, out var shuttleUid) &&
                TryComp<ShuttleComponent>(shuttleUid, out var shuttleComp) &&
                _station.GetOwningStation(shuttleUid.Value) == null)
            {
                // This shuttle has no station ownership but has saved data - assign it to this station
                _station.AddGridToStation(stationUid, shuttleUid.Value);
                shuttlesAssigned++;

                _sawmill.Info($"Assigned unowned shuttle {shipData.ShipName} ({shuttleUid}) to station {stationName}");
            }
        }

        if (shuttlesAssigned > 0)
        {
            _sawmill.Info($"Assigned {shuttlesAssigned} shuttles to station {stationName}");
        }
    }

    /// <summary>
    /// Handle cleanup when expedition data is removed
    /// </summary>
    private void OnExpeditionDataRemoved(EntityUid uid, SalvageExpeditionDataComponent component, ComponentShutdown args)
    {
        // Save the data before it's lost
        if (_persistentEntity != null && TryComp<RoundPersistenceComponent>(_persistentEntity.Value, out var persistence))
        {
            var stationName = MetaData(uid).EntityName;
            if (component.Missions.Count > 0 || component.ActiveMission != 0)
            {
                SaveStationData(uid, Comp<StationDataComponent>(uid), stationName, persistence);
                _sawmill.Info($"Emergency save of expedition data for {stationName}");
            }
        }
    }

    /// <summary>
    /// Handle cleanup when shuttle records are removed
    /// </summary>
    private void OnShuttleRecordsRemoved(EntityUid uid, ShuttleRecordsConsoleComponent component, ComponentShutdown args)
    {
        // This is handled in the main save process
    }

    /// <summary>
    /// Handle cleanup when station records are removed
    /// </summary>
    private void OnStationRecordsRemoved(EntityUid uid, StationRecordsComponent component, ComponentShutdown args)
    {
        // This is handled in the main save process
    }

    /// <summary>
    /// Public method to force save current data (for admin commands or emergency saves)
    /// </summary>
    public void ForceSave()
    {
        EnsurePersistentEntity();
        SaveAllCriticalData();
        _sawmill.Info("Forced save of persistent data completed");
    }

    /// <summary>
    /// Public method to get current persistence status
    /// </summary>
    public (int stationCount, int shipCount, int roundNumber) GetPersistenceStatus()
    {
        if (_persistentEntity == null || !TryComp<RoundPersistenceComponent>(_persistentEntity.Value, out var persistence))
            return (0, 0, 0);

        return (persistence.ExpeditionData.Count, persistence.ShipData.Count, persistence.RoundNumber);
    }

    /// <summary>
    /// Periodically update expedition console UIs to ensure timers and states stay current
    /// </summary>
    private void UpdateExpeditionUIs()
    {
        var expeditionQuery = AllEntityQuery<SalvageExpeditionDataComponent>();
        while (expeditionQuery.MoveNext(out var stationUid, out var expeditionComp))
        {
            // Update console UIs for this station
            var consoleQuery = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent, TransformComponent>();
            while (consoleQuery.MoveNext(out var consoleUid, out _, out var uiComp, out var xform))
            {
                var consoleStation = _station.GetOwningStation(consoleUid, xform);

                // Update consoles on the same station OR consoles on purchased shuttles
                // (shuttles have their own station entity that's different from expedition data station)
                if (consoleStation == stationUid || ShouldUpdateShuttleConsole(consoleUid, consoleStation, stationUid))
                {
                    var state = GetExpeditionState((stationUid, expeditionComp));
                    _ui.SetUiState((consoleUid, uiComp), SalvageConsoleUiKey.Expedition, state);
                }
            }
        }
    }

    /// <summary>
    /// Determines if an expedition console on a shuttle should be updated with expedition data from a station.
    /// This handles the case where purchased shuttles have their own station entity but should still
    /// receive expedition updates from their origin/purchasing station.
    /// </summary>
    private bool ShouldUpdateShuttleConsole(EntityUid consoleUid, EntityUid? consoleStation, EntityUid expeditionStation)
    {
        // If console is not on a station, skip it
        if (consoleStation == null)
            return false;

        // Check if the console is on a shuttle (has ShuttleComponent) that was purchased
        // We look for shuttles that have expedition consoles but are on a different station
        var consoleGrid = Transform(consoleUid).GridUid;
        if (consoleGrid == null)
            return false;

        // If this grid is a shuttle (has ShuttleComponent), allow updating from any expedition station
        // This ensures that expedition consoles on purchased shuttles get updates regardless of
        // which station the shuttle belongs to vs which station has the expedition data
        if (HasComp<ShuttleComponent>(consoleGrid.Value))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get the expedition console state for a station
    /// </summary>
    private SalvageExpeditionConsoleState GetExpeditionState(Entity<SalvageExpeditionDataComponent> expedition)
    {
        var component = expedition.Comp;
        var missions = component.Missions.Values.ToList();
        return new SalvageExpeditionConsoleState(
            component.NextOffer,
            component.Claimed,
            component.Cooldown,
            component.ActiveMission,
            missions,
            component.CanFinish,
            component.CooldownTime
        );
    }
}
