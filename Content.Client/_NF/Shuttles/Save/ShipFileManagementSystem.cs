using Content.Shared.Shuttles.Save;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Log;

namespace Content.Client.Shuttles.Save
{
    public sealed class ShipFileManagementSystem : EntitySystem
    {
        [Dependency] private readonly IClientNetManager _netManager = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<SendShipSaveDataClientMessage>(HandleSaveShipDataClient);
        }

        private void HandleSaveShipDataClient(SendShipSaveDataClientMessage message)
        {
            var filePath = Path.Combine("./SavedShips", $"{message.ShipName}.yml");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, message.YamlData);
            Logger.Info($"Client saved ship to: {filePath}");
        }

        public void RequestSaveShip(EntityUid deedUid)
        {
            RaiseNetworkEvent(new RequestSaveShipServerMessage((uint)deedUid.Id));
        }

        public async Task LoadShipFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Logger.Error($"File not found: {filePath}");
                return;
            }

            var yamlData = await File.ReadAllTextAsync(filePath);
            RaiseNetworkEvent(new RequestLoadShipMessage(yamlData));
            Logger.Info($"Client requested to load ship from: {filePath}");
        }

        public List<string> GetSavedShipFiles()
        {
            var savedShipsDir = "./SavedShips";
            if (!Directory.Exists(savedShipsDir))
            {
                return new List<string>();
            }
            return Directory.GetFiles(savedShipsDir, "*.yml").ToList();
        }
    }
}
