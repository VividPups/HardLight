using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.Events;

/// <summary>
///     Load a ship from the console. Contains the ship YAML data to load.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsoleLoadMessage : BoundUserInterfaceMessage
{
    public string YamlData { get; }

    public ShipyardConsoleLoadMessage(string yamlData)
    {
        YamlData = yamlData;
    }
}
