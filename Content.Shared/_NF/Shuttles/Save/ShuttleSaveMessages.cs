using Robust.Shared.Serialization;
using System;
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
        public string YamlData { get; }
        public string ShipName { get; }

        public SendShipSaveDataClientMessage(string yamlData, string shipName)
        {
            YamlData = yamlData;
            ShipName = shipName;
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
}
