using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Content.Shared.Shuttles.Save;
using Robust.Shared.Network;
using Robust.Shared.Maths;
using System;
using Content.Shared.Coordinates;
using Content.Shared.Coordinates.Helpers;

namespace Content.Server.Shuttles.Save
{
    public sealed class ShipSerializationSystem : EntitySystem
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly ISerializationManager _serializationManager = default!;

        private ISerializer _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        private IDeserializer _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        public override void Initialize()
        {
            base.Initialize();
        }

    public ShipGridData SerializeShip(EntityUid gridId, NetUserId playerId, string shipName)
        {
            if (!_entityManager.TryGetComponent<MapGridComponent>(gridId, out var grid))
            {
                throw new ArgumentException($"Grid with ID {gridId} not found.");
            }

            var shipGridData = new ShipGridData
            {
                Metadata = new ShipMetadata
                {
                    OriginalGridId = gridId.ToString(),
                    PlayerId = playerId.ToString(),
                    ShipName = shipName,
                    Timestamp = DateTime.UtcNow
                }
            };

            var gridData = new GridData
            {
                GridId = grid.Owner.ToString()
            };

            // Simplified tile serialization
            gridData.Tiles.Add(new TileData { X = 0, Y = 0, TileType = "Space" });

            // Simplified entity serialization
            foreach (var entity in _entityManager.EntityQuery<TransformComponent>())
            {
                if (entity.GridUid != gridId)
                    continue;

                var uid = entity.Owner;
                var meta = _entityManager.GetComponentOrNull<MetaDataComponent>(uid);
                var proto = meta?.EntityPrototype?.ID ?? string.Empty;

                var entityData = new EntityData
                {
                    EntityId = uid.ToString(),
                    Prototype = proto,
                    Position = entity.LocalPosition
                };

                gridData.Entities.Add(entityData);
            }

            shipGridData.Grids.Add(gridData);
            shipGridData.Metadata.Checksum = GenerateChecksum(shipGridData.Grids);

            return shipGridData;
        }

        public string SerializeShipGridDataToYaml(ShipGridData data)
        {
            return _serializer.Serialize(data);
        }

        public ShipGridData DeserializeShipGridDataFromYaml(string yamlString, Guid loadingPlayerId)
        {
            var data = _deserializer.Deserialize<ShipGridData>(yamlString);

            var actualChecksum = GenerateChecksum(data.Grids);
            if (data.Metadata.Checksum != actualChecksum)
            {
                throw new InvalidOperationException("Checksum mismatch! Ship data may have been tampered with.");
            }


            if (data.Metadata.PlayerId != loadingPlayerId.ToString())
            {
                throw new UnauthorizedAccessException("Player ID mismatch! You can only load ships you have saved.");
            }

            return data;
        }

        public EntityUid ReconstructShip(ShipGridData shipGridData)
        {
            if (shipGridData.Grids.Count == 0)
            {
                throw new ArgumentException("No grid data to reconstruct.");
            }

            var primaryGridData = shipGridData.Grids[0];
            var newGrid = _mapManager.CreateGrid(MapId.Nullspace);

            foreach (var entityData in primaryGridData.Entities)
            {
                var newEntity = _entityManager.SpawnEntity(entityData.Prototype, newGrid.Owner.ToCoordinates(entityData.Position));
            }

            return newGrid.Owner;
        }

        private string GenerateChecksum(List<GridData> grids)
        {
            var yamlString = _serializer.Serialize(grids);
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(yamlString));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
