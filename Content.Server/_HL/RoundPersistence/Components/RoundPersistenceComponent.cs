using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared.Shuttles.Components;
using Content.Shared.Salvage.Expeditions;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared.StationRecords;
using Content.Shared.CrewManifest;
using Robust.Shared.Serialization;
using System.Numerics;
using Robust.Shared.Utility;

namespace Content.Server._HL.RoundPersistence.Components;

/// <summary>
/// Component that stores critical data that needs to persist across round restarts.
/// This is applied to a persistent entity that survives round transitions.
/// </summary>
[RegisterComponent]
public sealed partial class RoundPersistenceComponent : Component
{
    /// <summary>
    /// Expedition system data for each station
    /// </summary>
    [DataField("expeditionData")]
    public Dictionary<string, PersistedExpeditionData> ExpeditionData = new();

    /// <summary>
    /// Shuttle records data for each station
    /// </summary>
    [DataField("shuttleRecords")]
    public Dictionary<string, List<ShuttleRecord>> ShuttleRecords = new();

    /// <summary>
    /// Station records and crew manifest data
    /// </summary>
    [DataField("stationRecords")]
    public Dictionary<string, PersistedStationRecords> StationRecords = new();

    /// <summary>
    /// Ship-station associations and IFF data
    /// </summary>
    [DataField("shipData")]
    public Dictionary<NetEntity, PersistedShipData> ShipData = new();

    /// <summary>
    /// Player payment tracking data
    /// </summary>
    [DataField("playerPayments")]
    public Dictionary<string, PersistedPlayerPayment> PlayerPayments = new();

    /// <summary>
    /// Console expedition data (independent of stations)
    /// </summary>
    [DataField("consoleData")]
    public Dictionary<string, StoredConsoleData> ConsoleData = new();

    /// <summary>
    /// Round number when this data was saved
    /// </summary>
    [DataField("roundNumber")]
    public int RoundNumber;

    /// <summary>
    /// Time when this data was last saved
    /// </summary>
    [DataField("lastSaveTime")]
    public TimeSpan LastSaveTime;
}

[DataDefinition, Serializable]
public sealed partial class PersistedExpeditionData
{
    [DataField("missions")]
    public Dictionary<ushort, PersistedMissionParams> Missions = new();

    [DataField("activeMission")]
    public ushort ActiveMission;

    [DataField("nextIndex")]
    public ushort NextIndex = 1;

    [DataField("cooldown")]
    public bool Cooldown = false;

    [DataField("nextOffer")]
    public TimeSpan NextOffer;

    [DataField("canFinish")]
    public bool CanFinish = false;

    [DataField("cooldownTime")]
    public TimeSpan CooldownTime;
}

[DataDefinition, Serializable]
public sealed partial class PersistedMissionParams
{
    [DataField("index")]
    public ushort Index;

    [DataField("seed")]
    public int Seed;

    [DataField("difficulty")]
    public string Difficulty = string.Empty;

    [DataField("missionType")]
    public int MissionType;
}

[DataDefinition, Serializable]
public sealed partial class StoredConsoleData
{
    [DataField("missions")]
    public Dictionary<ushort, PersistedMissionParams> Missions = new();

    [DataField("activeMission")]
    public ushort ActiveMission;

    [DataField("nextIndex")]
    public ushort NextIndex = 1;

    [DataField("cooldown")]
    public bool Cooldown = false;

    [DataField("nextOffer")]
    public TimeSpan NextOffer;

    [DataField("canFinish")]
    public bool CanFinish = false;

    [DataField("cooldownTime")]
    public TimeSpan CooldownTime;
}

[DataDefinition, Serializable]
public sealed partial class PersistedStationRecords
{
    [DataField("generalRecords")]
    public Dictionary<uint, GeneralStationRecord> GeneralRecords = new();

    [DataField("crewManifest")]
    public List<CrewManifestEntry> CrewManifest = new();

    [DataField("nextRecordId")]
    public uint NextRecordId = 1;

    [DataField("stationName")]
    public string StationName = string.Empty;

    [DataField("jobSlots")]
    public Dictionary<string, int?> JobSlots = new();

    [DataField("advertisement")]
    public string Advertisement = string.Empty;
}

[DataDefinition, Serializable]
public sealed partial class PersistedShipData
{
    [DataField("shipName")]
    public string ShipName = string.Empty;

    [DataField("ownerName")]
    public string OwnerName = string.Empty;

    [DataField("ownerUserId")]
    public string OwnerUserId = string.Empty;

    [DataField("stationAssociation")]
    public string StationAssociation = string.Empty;

    [DataField("iffFlags")]
    public IFFFlags IFFFlags;

    [DataField("iffColor")]
    public Color IFFColor;

    [DataField("purchasePrice")]
    public int PurchasePrice;

    [DataField("purchaseTime")]
    public DateTime? PurchaseTime;

    [DataField("isVoucherPurchase")]
    public bool IsVoucherPurchase;

    [DataField("shipClass")]
    public string ShipClass = string.Empty;

    [DataField("lastKnownPosition")]
    public Vector2? LastKnownPosition;

    [DataField("lastSeenTime")]
    public DateTime LastSeenTime;
}

[DataDefinition, Serializable]
public sealed partial class PersistedPlayerPayment
{
    [DataField("playerName")]
    public string PlayerName = string.Empty;

    [DataField("userId")]
    public string UserId = string.Empty;

    [DataField("currentJob")]
    public string CurrentJob = string.Empty;

    [DataField("totalHoursWorked")]
    public float TotalHoursWorked;

    [DataField("accumulatedPay")]
    public int AccumulatedPay;

    [DataField("lastPayment")]
    public DateTime LastPayment;

    [DataField("lastJobChange")]
    public DateTime LastJobChange;

    [DataField("isActive")]
    public bool IsActive = true;

    [DataField("lastStationAssociation")]
    public string LastStationAssociation = string.Empty;
}
