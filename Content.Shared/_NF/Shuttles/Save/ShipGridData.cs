using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;
using System.Numerics;
using System;
using System.Collections.Generic;

namespace Content.Shared.Shuttles.Save
{
    [Serializable]
    [DataDefinition]
    public sealed partial class ShipGridData // Added partial
    {
        [DataField("metadata")]
        public ShipMetadata Metadata { get; set; } = new();

        [DataField("grids")]
        public List<GridData> Grids { get; set; } = new();
    }

    [Serializable]
    [DataDefinition]
    public sealed partial class ShipMetadata // Added partial
    {
        [DataField("format_version")]
        public int FormatVersion { get; set; } = 1;

        [DataField("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;


    [DataField("original_grid_id")]
    public string OriginalGridId { get; set; } = string.Empty;

    [DataField("player_id")]
    public string PlayerId { get; set; } = string.Empty;

        [DataField("ship_name")]
        public string ShipName { get; set; } = string.Empty;

        [DataField("checksum")]
        public string Checksum { get; set; } = string.Empty;

        // Add other relevant metadata as needed, e.g., game version, server ID
    }

    [Serializable]
    [DataDefinition]
    public sealed partial class GridData // Added partial
    {

    [DataField("grid_id")]
    public string GridId { get; set; } = string.Empty;

        [DataField("tiles")]
        public List<TileData> Tiles { get; set; } = new();

        [DataField("entities")]
        public List<EntityData> Entities { get; set; } = new();
    }

    [Serializable]
    [DataDefinition]
    public sealed partial class TileData // Added partial
    {
        [DataField("x")]
        public int X { get; set; }

        [DataField("y")]
        public int Y { get; set; }

        [DataField("tile_type")]
        public string TileType { get; set; } = string.Empty; // This might need to be a more specific type or ID
    }

    [Serializable]
    [DataDefinition]
    public sealed partial class EntityData // Added partial
    {

    [DataField("entity_id")]
    public string EntityId { get; set; } = string.Empty;

        [DataField("prototype")]
        public string Prototype { get; set; } = string.Empty;

        [DataField("position")]
        public Vector2 Position { get; set; } = Vector2.Zero;

        [DataField("components")]
        public List<ComponentData> Components { get; set; } = new();
    }

    [Serializable]
    [DataDefinition]
    public sealed partial class ComponentData // Added partial
    {
        [DataField("type")]
        public string Type { get; set; } = string.Empty;

        // This will hold the serialized properties of the component.
        // RobustToolbox's serialization system can handle this automatically
        // if the properties are defined with [DataField] in the actual component class.
        // For a generic representation, you might need a more complex structure or dynamic serialization.
        // For simplicity, we'll assume RobustToolbox handles the inner serialization.
        [DataField("properties")]
        public Dictionary<string, object> Properties { get; set; } = new(); // Placeholder for actual component properties
    }
}
