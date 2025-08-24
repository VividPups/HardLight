using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._NF.Shipyard.Components;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;

namespace Content.Server.Shuttles.Save
{
    [AdminCommand(AdminFlags.Debug)]
    public sealed class SaveShipCommand : IConsoleCommand
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

        public string Command => "saveship";
        public string Description => "Save a ship from a shuttle deed";
        public string Help => "saveship <deed_entity_id>";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 1)
            {
                shell.WriteLine("Usage: saveship <deed_entity_id>");
                return;
            }

            if (!int.TryParse(args[0], out var entityIdInt))
            {
                shell.WriteLine("Invalid entity ID");
                return;
            }

            var entityUid = new EntityUid(entityIdInt);
            if (!_entityManager.EntityExists(entityUid))
            {
                shell.WriteLine("Entity does not exist");
                return;
            }

            if (!_entityManager.TryGetComponent<ShuttleDeedComponent>(entityUid, out var deedComponent))
            {
                shell.WriteLine("Entity is not a shuttle deed");
                return;
            }

            var shipSaveSystem = _entitySystemManager.GetEntitySystem<ShipSaveSystem>();
            // Trigger the save process manually
            // This will call the OnRequestSaveShipServer method in ShipSaveSystem
            shipSaveSystem.RequestSaveShip(entityUid, shell.Player); // Pass the deed Uid and player session

            shell.WriteLine($"Attempting to save ship for deed {entityUid}");
        }
    }
}
