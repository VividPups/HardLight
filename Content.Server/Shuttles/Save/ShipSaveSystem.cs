using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Content.Shared.Shuttles.Save;
using Content.Shared._NF.Shipyard.Components;
using System;
using Robust.Shared.Log;

namespace Content.Server.Shuttles.Save
{
    public sealed class ShipSaveSystem : EntitySystem
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
        [Dependency] private readonly IServerNetManager _netManager = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<RequestSaveShipServerMessage>(OnRequestSaveShipServer);
        }

        private void OnRequestSaveShipServer(RequestSaveShipServerMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;


            var deedUid = new EntityUid((int)msg.DeedUid);
            if (!_entityManager.TryGetComponent<ShuttleDeedComponent>(deedUid, out var deedComponent))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with invalid deed UID: {msg.DeedUid}");
                return;
            }

            if (deedComponent.ShuttleUid == null)
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with deed {msg.DeedUid} but no valid shuttle UID found.");
                return;
            }

            var shuttleUid = deedComponent.ShuttleUid.Value.ToEntityUid();
            if (!_entityManager.TryGetComponent<MapGridComponent>(shuttleUid, out var grid))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with deed {msg.DeedUid} but no valid shuttle UID found.");
                return;
            }

            var shipSerializationSystem = _entitySystemManager.GetEntitySystem<ShipSerializationSystem>();
            var shipName = deedComponent.ShuttleName ?? "SavedShip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var shipGridData = shipSerializationSystem.SerializeShip(shuttleUid, playerSession.UserId, shipName);
            var yamlString = shipSerializationSystem.SerializeShipGridDataToYaml(shipGridData);

            RaiseNetworkEvent(new SendShipSaveDataClientMessage(yamlString, shipName), playerSession);
            Logger.Info($"Sent serialized ship {shipName} to client {playerSession.Name} for saving.");

            _entityManager.DeleteEntity(shuttleUid);
            Logger.Info($"Deleted grid {shuttleUid} from server after saving.");
        }
    public void RequestSaveShip(EntityUid deedUid, ICommonSession? playerSession)
        {
            if (playerSession == null)
            {
                Logger.Warning($"Attempted to save ship for deed {deedUid} without a valid player session.");
                return;
            }

            if (!_entityManager.TryGetComponent<ShuttleDeedComponent>(deedUid, out var deedComponent))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with invalid deed UID: {deedUid}");
                return;
            }

            if (deedComponent.ShuttleUid is not { } shuttleUid || !_entityManager.TryGetComponent<MapGridComponent>(shuttleUid, out var grid))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with deed {deedUid} but no valid shuttle UID found.");
                return;
            }

            var shipSerializationSystem = _entitySystemManager.GetEntitySystem<ShipSerializationSystem>();
            var shipName = deedComponent.ShuttleName ?? "SavedShip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var shipGridData = shipSerializationSystem.SerializeShip(shuttleUid, playerSession.UserId, shipName);
            var yamlString = shipSerializationSystem.SerializeShipGridDataToYaml(shipGridData);

            RaiseNetworkEvent(new SendShipSaveDataClientMessage(yamlString, shipName), playerSession);
            Logger.Info($"Sent serialized ship {shipName} to client {playerSession.Name} for saving.");

            _entityManager.DeleteEntity(shuttleUid);
            Logger.Info($"Deleted grid {shuttleUid} from server after saving.");
        }
    }
}
