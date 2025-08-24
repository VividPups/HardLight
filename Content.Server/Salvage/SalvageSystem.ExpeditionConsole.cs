using Content.Shared.Shuttles.Components;
using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Dataset;
using Robust.Shared.Prototypes;
using Content.Shared.Popups; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Content.Server.Station.Components; // Frontier
using Robust.Shared.Map.Components; // Frontier
using Robust.Shared.Physics.Components; // Frontier
using Content.Shared.NPC; // Frontier
using Content.Server._NF.Salvage; // Frontier
using Content.Shared.NPC.Components; // Frontier
using Content.Server.Salvage.Expeditions; // Frontier
using Content.Shared.Mind.Components; // Frontier
using Content.Shared.Mobs.Components; // Frontier
using Robust.Shared.Physics; // Frontier
using Content.Server.Chat.Systems; // HARDLIGHT: For ChatSystem (server-side)
using Content.Shared.Salvage; // HARDLIGHT: For SalvageMissionType
using System.Threading; // HARDLIGHT: For CancellationTokenSource
using System.Linq; // HARDLIGHT: For ToList() and Take()
using Content.Shared.Shuttles.Systems; // HARDLIGHT: For FTLState
using Robust.Shared.Player; // HARDLIGHT: For Filter
using Content.Shared.Timing; // HARDLIGHT: For StartEndTime
using Robust.Shared.GameObjects; // HARDLIGHT: For SpawnTimer extension method

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    [ValidatePrototypeId<EntityPrototype>]
    public const string CoordinatesDisk = "CoordinatesDisk";

    [Dependency] private readonly SharedPopupSystem _popupSystem = default!; // Frontier
    [Dependency] private readonly SalvageSystem _salvage = default!; // Frontier
    [Dependency] private readonly ChatSystem _chatSystem = default!; // HARDLIGHT

    private const float ShuttleFTLMassThreshold = 50f; // Frontier
    private const float ShuttleFTLRange = 150f; // Frontier

    private void OnSalvageClaimMessage(EntityUid uid, SalvageExpeditionConsoleComponent component, ClaimSalvageMessage args)
    {
        // HARDLIGHT: Fully independent console system - no station dependencies
        var data = component.LocalExpeditionData;
        if (data == null)
        {
            // Initialize local data if it doesn't exist
            component.LocalExpeditionData = new SalvageExpeditionDataComponent();
            component.LocalExpeditionData.NextOffer = _timing.CurTime;
            GenerateLocalMissions(component.LocalExpeditionData);
            data = component.LocalExpeditionData;
        }

        // Skip if already claimed or invalid mission
        if (data.ActiveMission != 0 || !data.Missions.TryGetValue(args.Index, out var missionparams))
        {
            Log.Warning($"Mission claim rejected for console {ToPrettyString(uid)}: ActiveMission={data.ActiveMission}, HasMission={data.Missions.ContainsKey(args.Index)}, MissionCount={data.Missions.Count}, RequestedIndex={args.Index}");
            PlayDenySound((uid, component));

            // If console thinks it has an active mission but it might be stale, try to reset state
            if (data.ActiveMission != 0)
            {
                Log.Info($"Console {ToPrettyString(uid)} has stale active mission {data.ActiveMission}, attempting to reset state");
                data.ActiveMission = 0;
                data.CanFinish = false;
                data.Cooldown = false;

                // Regenerate missions to ensure fresh state after a short delay
                uid.SpawnTimer(TimeSpan.FromMilliseconds(100), () =>
                {
                    GenerateLocalMissions(data);
                    UpdateConsole((uid, component));
                });
            }

            UpdateConsole((uid, component));
            return;
        }

        // Find the grid this console is on
        if (!TryComp<TransformComponent>(uid, out var consoleXform))
        {
            Log.Error($"Console {ToPrettyString(uid)} has no transform component");
            PlayDenySound((uid, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), uid, PopupType.MediumCaution);
            UpdateConsole((uid, component));
            return;
        }

        var ourGrid = consoleXform.GridUid;
        if (ourGrid == null || !TryComp<MapGridComponent>(ourGrid, out var gridComp))
        {
            Log.Error($"Console {ToPrettyString(uid)} grid {ourGrid} has no map grid component");
            PlayDenySound((uid, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), uid, PopupType.MediumCaution);
            UpdateConsole((uid, component));
            return;
        }

        // Store reference to console in mission params for FTL completion tracking
        component.ActiveConsole = uid;

        // Directly spawn the mission - console is completely independent
        try
        {
            Log.Info($"Spawning mission {args.Index} ({missionparams.MissionType}) for independent console {ToPrettyString(uid)} on grid {ourGrid}");
            SpawnMissionForConsole(missionparams, ourGrid.Value, uid);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to spawn mission for console {ToPrettyString(uid)}: {ex}");
            return; // Don't mark as claimed if spawning failed
        }

        // Mark as claimed and active - console handles its own state
        data.ActiveMission = args.Index;
        data.CanFinish = false; // Will be set to true when FTL completes

        var mission = GetMission(missionparams.MissionType, _prototypeManager.Index<SalvageDifficultyPrototype>(missionparams.Difficulty), missionparams.Seed);
        data.NextOffer = _timing.CurTime + mission.Duration + TimeSpan.FromSeconds(1);
        data.CooldownTime = mission.Duration + TimeSpan.FromSeconds(1);

        UpdateConsole((uid, component));

        // Announce to all players on this grid only
        if (consoleXform.GridUid != null)
        {
            var filter = Filter.Empty().AddInGrid(consoleXform.GridUid.Value);
            var announcement = Loc.GetString("salvage-expedition-announcement-claimed");
            _chatSystem.DispatchFilteredAnnouncement(filter, announcement, uid,
                sender: "Expedition Console", colorOverride: Color.LightBlue);
        }

        Log.Info($"Mission {args.Index} successfully claimed on independent console {ToPrettyString(uid)}");
    }

    // Frontier: early expedition end
    private void OnSalvageFinishMessage(EntityUid entity, SalvageExpeditionConsoleComponent component, FinishSalvageMessage e)
    {
        // HARDLIGHT: Fully independent console system - no station dependencies
        var data = component.LocalExpeditionData;
        if (data == null || !data.CanFinish)
        {
            PlayDenySound((entity, component));
            UpdateConsole((entity, component));
            return;
        }

        // Get the console's grid
        if (!TryComp(entity, out TransformComponent? xform))
        {
            PlayDenySound((entity, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), entity, PopupType.MediumCaution);
            UpdateConsole((entity, component));
            return;
        }

        // Complete the expedition - console handles its own state independently
        data.CanFinish = false;
        data.ActiveMission = 0;
        data.Cooldown = false;

        // Update console first to show cleared state
        UpdateConsole((entity, component));

        // Generate new missions after a short delay to prevent visual glitches
        entity.SpawnTimer(TimeSpan.FromMilliseconds(500), () =>
        {
            if (Exists(entity) && TryComp<SalvageExpeditionConsoleComponent>(entity, out var comp) && comp.LocalExpeditionData != null)
            {
                GenerateLocalMissions(comp.LocalExpeditionData);
                UpdateConsole((entity, comp));
            }
        });

        // Reset FTL component to cooldown state to allow immediate re-expedition
        if (xform.GridUid != null && TryComp<FTLComponent>(xform.GridUid.Value, out var ftlComp))
        {
            ftlComp.State = FTLState.Cooldown;
            ftlComp.StateTime = new StartEndTime { Start = TimeSpan.Zero, End = TimeSpan.Zero }; // Immediate cooldown completion
            Dirty(xform.GridUid.Value, ftlComp);
        }

        UpdateConsole((entity, component));

        // Announce completion to grid only
        if (xform.GridUid != null)
        {
            var filter = Filter.Empty().AddInGrid(xform.GridUid.Value);
            var announcement = Loc.GetString("salvage-expedition-announcement-completed");
            _chatSystem.DispatchFilteredAnnouncement(filter, announcement, entity,
                sender: "Expedition Console", colorOverride: Color.Green);
        }

        Log.Info($"Expedition finished independently on console {ToPrettyString(entity)}");
    }
    // End Frontier: early expedition end

    private void OnSalvageConsoleInit(Entity<SalvageExpeditionConsoleComponent> console, ref ComponentInit args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleParent(Entity<SalvageExpeditionConsoleComponent> console, ref EntParentChangedMessage args)
    {
        UpdateConsole(console);
    }

    private void UpdateConsoles(Entity<SalvageExpeditionDataComponent> component)
    {
        // HARDLIGHT: This method is obsolete with independent console system
        // Each console manages its own state independently
        Log.Debug("UpdateConsoles called but consoles are now independent - no action needed");
    }

    private void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> component)
    {
        // HARDLIGHT: Fully independent console system - no station dependencies
        var consoleComp = component.Comp;
        var uid = component.Owner;

        // Initialize local data if it doesn't exist
        if (consoleComp.LocalExpeditionData == null)
        {
            Log.Info($"Initializing independent expedition data for console {ToPrettyString(uid)}");
            consoleComp.LocalExpeditionData = new SalvageExpeditionDataComponent();
            consoleComp.LocalExpeditionData.NextOffer = _timing.CurTime;
            consoleComp.LocalExpeditionData.Cooldown = false;
            consoleComp.LocalExpeditionData.ActiveMission = 0;
            consoleComp.LocalExpeditionData.CanFinish = false;
            consoleComp.LocalExpeditionData.CooldownTime = TimeSpan.Zero;
            GenerateLocalMissions(consoleComp.LocalExpeditionData);
        }

        var data = consoleComp.LocalExpeditionData;

        // Generate missions if needed
        if (data.Missions.Count == 0 && data.NextOffer < _timing.CurTime)
        {
            GenerateLocalMissions(data);
        }

        // Always create functional state - console operates independently
        var state = new SalvageExpeditionConsoleState(
            data.NextOffer,
            data.ActiveMission != 0,
            false, // Never disable - console is independent
            data.ActiveMission,
            data.Missions.Values.ToList(),
            data.CanFinish,
            data.CooldownTime
        );

        if (!TryComp<UserInterfaceComponent>(uid, out var uiComp))
        {
            Log.Warning($"Console {ToPrettyString(uid)} has no UserInterfaceComponent");
            return;
        }

        _ui.SetUiState(component.Owner, SalvageConsoleUiKey.Expedition, state);
        Log.Debug($"Updated independent console {ToPrettyString(uid)} with {state.Missions.Count} missions");
    }

    // HARDLIGHT: Direct mission spawning for console-specific expeditions
    private void SpawnMissionForConsole(SalvageMissionParams missionParams, EntityUid shuttleGrid, EntityUid consoleUid)
    {
        // HARDLIGHT: Fully independent console system - no station dependencies
        Log.Info($"Spawning independent mission for console {consoleUid} on shuttle {shuttleGrid}");

        // Directly spawn the mission using the existing job system
        // HARDLIGHT: For independent console system, use shuttle as station and pass console reference
        var missionStation = shuttleGrid; // Always use shuttle grid for independent consoles
        var cancelToken = new CancellationTokenSource();
        var job = new SpawnSalvageMissionJob(
            SalvageJobTime,
            EntityManager,
            _timing,
            _logManager,
            _prototypeManager,
            _anchorable,
            _biome,
            _dungeon,
            _metaData,
            _mapSystem,
            _station,
            _shuttle,
            this,
            missionStation,
            consoleUid, // HARDLIGHT: Pass console reference for FTL targeting
            null, // No coordinates disk for console missions
            missionParams,
            cancelToken.Token);

        _salvageJobs.Add((job, cancelToken));
        _salvageQueue.EnqueueJob(job);
    }

    // HARDLIGHT: Self-sufficient mission generation for individual consoles
    private void GenerateLocalMissions(SalvageExpeditionDataComponent component)
    {
        try
        {
            component.Missions.Clear();
            Log.Debug($"Generating local missions for expedition console (clearing {component.Missions.Count} existing missions)");

            // Force generate missions regardless of any dependencies
            var missionDifficulties = new List<(ProtoId<SalvageDifficultyPrototype> id, int value)>
            {
                ("NFModerate", 0),
                ("NFHazardous", 1),
                ("NFExtreme", 2)
            };

            if (missionDifficulties.Count <= 0)
            {
                Log.Warning("No mission difficulties available, using fallback mission generation");
                // Fallback if prototypes are missing - create basic missions anyway
                for (var i = 0; i < 6; i++) // Use hardcoded limit for expedition console
                {
                    var mission = new SalvageMissionParams
                    {
                        Index = component.NextIndex,
                        MissionType = (SalvageMissionType)_random.NextByte((byte)SalvageMissionType.Max + 1),
                        Seed = _random.Next(),
                        Difficulty = "NFModerate", // Default fallback
                    };
                    component.Missions[component.NextIndex++] = mission;
                }
                Log.Info($"Generated {component.Missions.Count} fallback missions");
                return;
            }

            _random.Shuffle(missionDifficulties);
            var difficulties = missionDifficulties.Take(6).ToList(); // Use hardcoded limit for expedition console

            while (difficulties.Count < 6) // Use hardcoded limit for expedition console
            {
                var difficultyIndex = _random.Next(missionDifficulties.Count);
                difficulties.Add(missionDifficulties[difficultyIndex]);
            }
            difficulties.Sort((x, y) => Comparer<int>.Default.Compare(x.value, y.value));

            Log.Debug($"Selected difficulties: {string.Join(", ", difficulties.Select(d => d.id))}");

            for (var i = 0; i < 6; i++) // Use hardcoded limit for expedition console
            {
                var mission = new SalvageMissionParams
                {
                    Index = component.NextIndex,
                    MissionType = (SalvageMissionType)_random.NextByte((byte)SalvageMissionType.Max + 1),
                    Seed = _random.Next(),
                    Difficulty = difficulties[i].id,
                };
                component.Missions[component.NextIndex++] = mission;
                Log.Debug($"Generated mission {mission.Index}: {mission.MissionType} ({mission.Difficulty})");
            }

            Log.Info($"Successfully generated {component.Missions.Count} missions for expedition console");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to generate local missions for expedition console: {ex}");

            // Emergency fallback - create at least one basic mission
            component.Missions.Clear();
            var emergencyMission = new SalvageMissionParams
            {
                Index = component.NextIndex,
                MissionType = SalvageMissionType.Destruction,
                Seed = _random.Next(),
                Difficulty = "NFModerate",
            };
            component.Missions[component.NextIndex++] = emergencyMission;
            Log.Warning($"Created emergency fallback mission {emergencyMission.Index}");
        }
    }

    // Frontier: deny sound
    private void PlayDenySound(Entity<SalvageExpeditionConsoleComponent> ent)
    {
        _audio.PlayPvs(_audio.ResolveSound(ent.Comp.ErrorSound), ent);
    }
    // End Frontier
}
