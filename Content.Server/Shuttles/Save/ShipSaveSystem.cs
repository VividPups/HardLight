using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Shuttles.Save;
using Content.Shared._NF.Shipyard.Components;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
            SubscribeNetworkEvent<RequestLoadShipMessage>(OnRequestLoadShip);
            SubscribeNetworkEvent<RequestAvailableShipsMessage>(OnRequestAvailableShips);
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

            if (!_entityManager.TryGetEntity(deedComponent.ShuttleUid.Value, out var shuttleUid))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with deed {msg.DeedUid} but failed to convert NetEntity to EntityUid.");
                return;
            }
            if (!_entityManager.TryGetComponent<MapGridComponent>(shuttleUid.Value, out var grid))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with deed {msg.DeedUid} but no valid shuttle UID found.");
                return;
            }

            var shipSerializationSystem = _entitySystemManager.GetEntitySystem<ShipSerializationSystem>();
            var shipName = deedComponent.ShuttleName ?? "SavedShip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var shipGridData = shipSerializationSystem.SerializeShip(shuttleUid.Value, playerSession.UserId, shipName);
            var yamlString = shipSerializationSystem.SerializeShipGridDataToYaml(shipGridData);

            // Send ship data to client for local saving
            RaiseNetworkEvent(new SendShipSaveDataClientMessage(shipName, yamlString), playerSession);
            Logger.Info($"Sent serialized ship {shipName} to client {playerSession.Name} for local saving.");

            // Delete the ship after successful save
            _entityManager.DeleteEntity(shuttleUid.Value);
            Logger.Info($"Deleted grid {shuttleUid.Value} from server after saving.");
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

            if (deedComponent.ShuttleUid == null || !_entityManager.TryGetEntity(deedComponent.ShuttleUid.Value, out var shuttleUid) || !_entityManager.TryGetComponent<MapGridComponent>(shuttleUid.Value, out var grid))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with deed {deedUid} but no valid shuttle UID found.");
                return;
            }

            var shipSerializationSystem = _entitySystemManager.GetEntitySystem<ShipSerializationSystem>();
            var shipName = deedComponent.ShuttleName ?? "SavedShip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var shipGridData = shipSerializationSystem.SerializeShip(shuttleUid.Value, playerSession.UserId, shipName);
            var yamlString = shipSerializationSystem.SerializeShipGridDataToYaml(shipGridData);

            // Send ship data to client for local saving
            RaiseNetworkEvent(new SendShipSaveDataClientMessage(shipName, yamlString), playerSession);
            Logger.Info($"Sent serialized ship {shipName} to client {playerSession.Name} for local saving.");

            // Delete the ship after successful save
            _entityManager.DeleteEntity(shuttleUid.Value);
            Logger.Info($"Deleted grid {shuttleUid.Value} from server after saving.");
        }

        private void OnRequestLoadShip(RequestLoadShipMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;

            Logger.Info($"Player {playerSession.Name} requested to load ship from YAML data");
            
            // TODO: Implement ship loading from saved files
            // This would involve deserializing the ship data and spawning it in the game world
            // For now, we just log the request
        }

        private void OnRequestAvailableShips(RequestAvailableShipsMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;

            // Client handles available ships from local user data
            Logger.Info($"Player {playerSession.Name} requested available ships - client handles this locally");
        }
    }
}
