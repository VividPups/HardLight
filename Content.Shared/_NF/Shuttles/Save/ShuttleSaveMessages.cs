using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Content.Shared.Shuttles.Save
{
    [Serializable, NetSerializable]
    public sealed class RequestSaveShipServerMessage : EntityEventArgs
    {
        public uint DeedUid { get; }

        public RequestSaveShipServerMessage(uint deedUid)
        {
            DeedUid = deedUid;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SendShipSaveDataClientMessage : EntityEventArgs
    {
        public string ShipName { get; }
        public string ShipData { get; }

        public SendShipSaveDataClientMessage(string shipName, string shipData)
        {
            ShipName = shipName;
            ShipData = shipData;
        }
    }

    [Serializable, NetSerializable]
    public sealed class RequestLoadShipMessage : EntityEventArgs
    {
        public string YamlData { get; }

        public RequestLoadShipMessage(string yamlData)
        {
            YamlData = yamlData;
        }
    }

    [Serializable, NetSerializable]
    public sealed class RequestAvailableShipsMessage : EntityEventArgs
    {
    }

    [Serializable, NetSerializable]
    public sealed class SendAvailableShipsMessage : EntityEventArgs
    {
        public List<string> ShipNames { get; }

        public SendAvailableShipsMessage(List<string> shipNames)
        {
            ShipNames = shipNames;
        }
    }
}
