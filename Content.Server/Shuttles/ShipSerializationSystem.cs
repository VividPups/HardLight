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
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Content.Shared.Shuttles.Save;
using Robust.Shared.Network;
using Robust.Shared.Maths;
using System;
using Content.Shared.Coordinates;
using Content.Shared.Coordinates.Helpers;
using Robust.Shared.Log;
using Robust.Server.GameObjects;

namespace Content.Server.Shuttles.Save
{
    public sealed class ShipSerializationSystem : EntitySystem
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly ISerializationManager _serializationManager = default!;
        [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
        [Dependency] private readonly MapSystem _map = default!;
        
        private ISawmill _sawmill = default!;

        private ISerializer _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        private IDeserializer _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        public override void Initialize()
        {
            base.Initialize();
            _sawmill = Logger.GetSawmill("ship-serialization");
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

            // Proper tile serialization
            var tiles = _map.GetAllTiles(gridId, grid);
            foreach (var tile in tiles)
            {
                var tileDef = _tileDefManager[tile.Tile.TypeId];
                if (tileDef.ID == "Space") // Skip space tiles
                    continue;

                gridData.Tiles.Add(new TileData 
                { 
                    X = tile.GridIndices.X, 
                    Y = tile.GridIndices.Y, 
                    TileType = tileDef.ID 
                });
            }

            _sawmill.Info($"Serialized {gridData.Tiles.Count} tiles");

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
            _sawmill.Info($"Deserializing YAML for player {loadingPlayerId}");
            ShipGridData data;
            try
            {
                data = _deserializer.Deserialize<ShipGridData>(yamlString);
                _sawmill.Debug($"Successfully deserialized YAML data");
            }
            catch (Exception ex)
            {
                _sawmill.Error($"YAML deserialization failed: {ex.Message}");
                throw;
            }

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

        public EntityUid ReconstructShipOnMap(ShipGridData shipGridData, MapId targetMap, Vector2 offset)
        {
            _sawmill.Info($"Reconstructing ship with {shipGridData.Grids.Count} grids on map {targetMap} at offset {offset}");
            if (shipGridData.Grids.Count == 0)
            {
                throw new ArgumentException("No grid data to reconstruct.");
            }

            var primaryGridData = shipGridData.Grids[0];
            _sawmill.Info($"Primary grid has {primaryGridData.Entities.Count} entities");
            _sawmill.Info($"Primary grid has {primaryGridData.Tiles.Count} tiles");
            
            var newGrid = _mapManager.CreateGrid(targetMap);
            _sawmill.Info($"Created new grid {newGrid.Owner} on shipyard map {targetMap}");

            // Move grid to the specified offset position
            var gridXform = Transform(newGrid.Owner);
            gridXform.WorldPosition = offset;

            // Reconstruct tiles first
            foreach (var tileData in primaryGridData.Tiles)
            {
                if (string.IsNullOrEmpty(tileData.TileType) || tileData.TileType == "Space")
                    continue;

                try
                {
                    var tileDef = _tileDefManager[tileData.TileType];
                    var tile = new Tile(tileDef.TileId);
                    var tileCoords = new Vector2i(tileData.X, tileData.Y);
                    _map.SetTile(newGrid.Owner, newGrid, tileCoords, tile);
                    _sawmill.Debug($"Placed tile {tileData.TileType} at {tileCoords}");
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to place tile {tileData.TileType} at ({tileData.X}, {tileData.Y}): {ex.Message}");
                }
            }

            foreach (var entityData in primaryGridData.Entities)
            {
                // Skip entities with empty or null prototypes
                if (string.IsNullOrEmpty(entityData.Prototype))
                {
                    _sawmill.Debug($"Skipping entity with empty prototype at {entityData.Position}");
                    continue;
                }

                _sawmill.Debug($"Reconstructing entity: {entityData.Prototype} at {entityData.Position}");
                try
                {
                    var coordinates = new EntityCoordinates(newGrid.Owner, entityData.Position);
                    var newEntity = _entityManager.SpawnEntity(entityData.Prototype, coordinates);
                    _sawmill.Debug($"Spawned entity {newEntity}");
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to spawn entity {entityData.Prototype}: {ex.Message}");
                    throw;
                }
            }

            return newGrid.Owner;
        }

        public EntityUid ReconstructShip(ShipGridData shipGridData)
        {
            _sawmill.Info($"Reconstructing ship with {shipGridData.Grids.Count} grids");
            if (shipGridData.Grids.Count == 0)
            {
                throw new ArgumentException("No grid data to reconstruct.");
            }

            var primaryGridData = shipGridData.Grids[0];
            _sawmill.Info($"Primary grid has {primaryGridData.Entities.Count} entities");
            
            // Create a new map for the ship instead of using MapId.Nullspace
            _map.CreateMap(out var mapId);
            _sawmill.Info($"Created new map {mapId}");
            
            var newGrid = _mapManager.CreateGrid(mapId);
            _sawmill.Info($"Created new grid {newGrid.Owner} on map {mapId}");

            foreach (var entityData in primaryGridData.Entities)
            {
                // Skip entities with empty or null prototypes
                if (string.IsNullOrEmpty(entityData.Prototype))
                {
                    _sawmill.Debug($"Skipping entity with empty prototype at {entityData.Position}");
                    continue;
                }

                _sawmill.Debug($"Reconstructing entity: {entityData.Prototype} at {entityData.Position}");
                try
                {
                    var coordinates = new EntityCoordinates(newGrid.Owner, entityData.Position);
                    var newEntity = _entityManager.SpawnEntity(entityData.Prototype, coordinates);
                    _sawmill.Debug($"Spawned entity {newEntity}");
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to spawn entity {entityData.Prototype}: {ex.Message}");
                    throw;
                }
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
