using Content.Server.Administration;
using Content.Server.Shuttles.Save;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Player;
using Robust.Shared.IoC;
using System.IO;
using System.Linq;
using Robust.Server.Player;
using Content.Shared.Shuttles.Save;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Content.Server.Administration.Commands;

/// <summary>
/// Admin commands for moderating ship save files
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ShipSaveListCommand : IConsoleCommand
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    
    public string Command => "shipsave_list";
    public string Description => "List all ship save files on the server";
    public string Help => "shipsave_list [player_name]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var resourceManager = IoCManager.Resolve<Robust.Shared.ContentPack.IResourceManager>();
        var userDataPath = resourceManager.UserData.ToString() ?? "";
        var savedShipsPath = Path.Combine(userDataPath, "saved_ships");

        if (!Directory.Exists(savedShipsPath))
        {
            shell.WriteLine("No saved ships directory found.");
            return;
        }

        var shipFiles = Directory.GetFiles(savedShipsPath, "*.yml");
        if (shipFiles.Length == 0)
        {
            shell.WriteLine("No ship save files found.");
            return;
        }

        shell.WriteLine($"Found {shipFiles.Length} ship save files:");

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var targetPlayer = args.Length > 0 ? args[0] : null;

        foreach (var filePath in shipFiles)
        {
            try
            {
                var yamlContent = File.ReadAllText(filePath);
                var shipData = deserializer.Deserialize<ShipGridData>(yamlContent);
                var fileName = Path.GetFileName(filePath);

                // Filter by player if specified
                if (targetPlayer != null && !shipData.Metadata.PlayerId.Contains(targetPlayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileSize = new FileInfo(filePath).Length;
                shell.WriteLine($"  {fileName}:");
                shell.WriteLine($"    Ship Name: {shipData.Metadata.ShipName}");
                shell.WriteLine($"    Player ID: {shipData.Metadata.PlayerId}");
                shell.WriteLine($"    Timestamp: {shipData.Metadata.Timestamp:yyyy-MM-dd HH:mm:ss}");
                shell.WriteLine($"    File Size: {fileSize / 1024.0:F1} KB");
                shell.WriteLine($"    Entities: {shipData.Grids.Sum(g => g.Entities.Count)}");
                shell.WriteLine($"    Containers: {shipData.Grids.Sum(g => g.Entities.Count(e => e.IsContainer))}");
                shell.WriteLine($"    Contained Items: {shipData.Grids.Sum(g => g.Entities.Count(e => e.IsContained))}");
                shell.WriteLine("");
            }
            catch (Exception ex)
            {
                shell.WriteLine($"  {Path.GetFileName(filePath)}: ERROR - {ex.Message}");
            }
        }
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipSaveDeleteCommand : IConsoleCommand
{
    public string Command => "shipsave_delete";
    public string Description => "Delete a ship save file";
    public string Help => "shipsave_delete <filename>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_delete <filename>");
            return;
        }

        var resourceManager = IoCManager.Resolve<Robust.Shared.ContentPack.IResourceManager>();
        var userDataPath = resourceManager.UserData.ToString() ?? "";
        var savedShipsPath = Path.Combine(userDataPath, "saved_ships");
        var filePath = Path.Combine(savedShipsPath, args[0]);

        if (!File.Exists(filePath))
        {
            shell.WriteLine($"Ship save file '{args[0]}' not found.");
            return;
        }

        try
        {
            File.Delete(filePath);
            shell.WriteLine($"Successfully deleted ship save file '{args[0]}'");
        }
        catch (Exception ex)
        {
            shell.WriteLine($"Failed to delete ship save file: {ex.Message}");
        }
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipSaveInspectCommand : IConsoleCommand
{
    public string Command => "shipsave_inspect";
    public string Description => "Inspect detailed information about a ship save file";
    public string Help => "shipsave_inspect <filename>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_inspect <filename>");
            return;
        }

        var resourceManager = IoCManager.Resolve<Robust.Shared.ContentPack.IResourceManager>();
        var userDataPath = resourceManager.UserData.ToString() ?? "";
        var savedShipsPath = Path.Combine(userDataPath, "saved_ships");
        var filePath = Path.Combine(savedShipsPath, args[0]);

        if (!File.Exists(filePath))
        {
            shell.WriteLine($"Ship save file '{args[0]}' not found.");
            return;
        }

        try
        {
            var yamlContent = File.ReadAllText(filePath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var shipData = deserializer.Deserialize<ShipGridData>(yamlContent);

            shell.WriteLine($"=== Ship Save Inspection: {args[0]} ===");
            shell.WriteLine($"Ship Name: {shipData.Metadata.ShipName}");
            shell.WriteLine($"Player ID: {shipData.Metadata.PlayerId}");
            shell.WriteLine($"Original Grid ID: {shipData.Metadata.OriginalGridId}");
            shell.WriteLine($"Timestamp: {shipData.Metadata.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");
            shell.WriteLine($"Format Version: {shipData.Metadata.FormatVersion}");
            shell.WriteLine($"Checksum: {shipData.Metadata.Checksum}");
            shell.WriteLine("");

            for (int gridIndex = 0; gridIndex < shipData.Grids.Count; gridIndex++)
            {
                var grid = shipData.Grids[gridIndex];
                shell.WriteLine($"Grid {gridIndex + 1}:");
                shell.WriteLine($"  Grid ID: {grid.GridId}");
                shell.WriteLine($"  Tiles: {grid.Tiles.Count}");
                shell.WriteLine($"  Total Entities: {grid.Entities.Count}");
                shell.WriteLine($"  Container Entities: {grid.Entities.Count(e => e.IsContainer)}");
                shell.WriteLine($"  Contained Entities: {grid.Entities.Count(e => e.IsContained)}");
                shell.WriteLine($"  Has Atmosphere Data: {!string.IsNullOrEmpty(grid.AtmosphereData)}");
                shell.WriteLine($"  Has Decal Data: {!string.IsNullOrEmpty(grid.DecalData)}");

                // Component analysis
                var componentTypes = grid.Entities
                    .SelectMany(e => e.Components)
                    .GroupBy(c => c.Type)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .ToList();

                if (componentTypes.Any())
                {
                    shell.WriteLine($"  Top Component Types:");
                    foreach (var componentType in componentTypes)
                    {
                        shell.WriteLine($"    {componentType.Key}: {componentType.Count()}");
                    }
                }

                // Entity prototype analysis
                var entityTypes = grid.Entities
                    .GroupBy(e => e.Prototype)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .ToList();

                shell.WriteLine($"  Top Entity Types:");
                foreach (var entityType in entityTypes)
                {
                    shell.WriteLine($"    {entityType.Key}: {entityType.Count()}");
                }
                shell.WriteLine("");
            }
        }
        catch (Exception ex)
        {
            shell.WriteLine($"Failed to inspect ship save file: {ex.Message}");
        }
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipSaveValidateCommand : IConsoleCommand
{
    public string Command => "shipsave_validate";
    public string Description => "Validate ship save file integrity and checksums";
    public string Help => "shipsave_validate <filename>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_validate <filename>");
            return;
        }

        var resourceManager = IoCManager.Resolve<Robust.Shared.ContentPack.IResourceManager>();
        var userDataPath = resourceManager.UserData.ToString() ?? "";
        var savedShipsPath = Path.Combine(userDataPath, "saved_ships");
        var filePath = Path.Combine(savedShipsPath, args[0]);

        if (!File.Exists(filePath))
        {
            shell.WriteLine($"Ship save file '{args[0]}' not found.");
            return;
        }

        try
        {
            var yamlContent = File.ReadAllText(filePath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var shipData = deserializer.Deserialize<ShipGridData>(yamlContent);
            
            var shipSerializationSystem = IoCManager.Resolve<ShipSerializationSystem>();
            
            // Try to deserialize and validate
            var validatedData = shipSerializationSystem.DeserializeShipGridDataFromYaml(yamlContent, Guid.Parse(shipData.Metadata.PlayerId), out var wasConverted);
            
            shell.WriteLine($"=== Validation Results for {args[0]} ===");
            shell.WriteLine($"✓ YAML parsing: SUCCESS");
            shell.WriteLine($"✓ Checksum validation: SUCCESS");
            if (wasConverted)
            {
                shell.WriteLine($"⚠ Legacy format detected and converted");
            }
            shell.WriteLine($"✓ Structure validation: SUCCESS");
            shell.WriteLine("");

            // Detailed validation
            for (int gridIndex = 0; gridIndex < validatedData.Grids.Count; gridIndex++)
            {
                var grid = validatedData.Grids[gridIndex];
                shell.WriteLine($"Grid {gridIndex + 1} Validation:");
                
                // Container relationship validation
                var containerEntities = grid.Entities.Where(e => e.IsContainer).ToList();
                var containedEntities = grid.Entities.Where(e => e.IsContained).ToList();
                var orphanedEntities = 0;
                var invalidContainers = 0;

                foreach (var containedEntity in containedEntities)
                {
                    if (string.IsNullOrEmpty(containedEntity.ParentContainerEntity))
                    {
                        orphanedEntities++;
                        continue;
                    }

                    var parentExists = grid.Entities.Any(e => e.EntityId == containedEntity.ParentContainerEntity);
                    if (!parentExists)
                    {
                        orphanedEntities++;
                    }
                }

                shell.WriteLine($"  Container Entities: {containerEntities.Count}");
                shell.WriteLine($"  Contained Entities: {containedEntities.Count}");
                if (orphanedEntities > 0)
                {
                    shell.WriteLine($"  ⚠ Orphaned Entities: {orphanedEntities}");
                }
                if (invalidContainers > 0)
                {
                    shell.WriteLine($"  ⚠ Invalid Containers: {invalidContainers}");
                }

                shell.WriteLine($"  ✓ Total Entities: {grid.Entities.Count}");
                shell.WriteLine($"  ✓ Total Tiles: {grid.Tiles.Count}");
                shell.WriteLine("");
            }
        }
        catch (Exception ex)
        {
            shell.WriteLine($"❌ VALIDATION FAILED: {ex.Message}");
        }
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipSaveCleanupCommand : IConsoleCommand
{
    public string Command => "shipsave_cleanup";
    public string Description => "Clean up old or corrupted ship save files";
    public string Help => "shipsave_cleanup [--dry-run] [--older-than-days=30]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var dryRun = args.Any(arg => arg == "--dry-run");
        var olderThanDays = 30;
        
        foreach (var arg in args)
        {
            if (arg.StartsWith("--older-than-days="))
            {
                if (int.TryParse(arg.Substring("--older-than-days=".Length), out var days))
                {
                    olderThanDays = days;
                }
            }
        }

        var resourceManager = IoCManager.Resolve<Robust.Shared.ContentPack.IResourceManager>();
        var userDataPath = resourceManager.UserData.ToString() ?? "";
        var savedShipsPath = Path.Combine(userDataPath, "saved_ships");

        if (!Directory.Exists(savedShipsPath))
        {
            shell.WriteLine("No saved ships directory found.");
            return;
        }

        var shipFiles = Directory.GetFiles(savedShipsPath, "*.yml");
        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
        var toDelete = new List<string>();
        var corrupted = new List<string>();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        foreach (var filePath in shipFiles)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                
                // Check if file is older than cutoff
                if (fileInfo.LastWriteTimeUtc < cutoffDate)
                {
                    toDelete.Add(filePath);
                    continue;
                }

                // Check if file is corrupted
                var yamlContent = File.ReadAllText(filePath);
                var shipData = deserializer.Deserialize<ShipGridData>(yamlContent);
                
                // Basic corruption checks
                if (string.IsNullOrEmpty(shipData.Metadata.ShipName) ||
                    string.IsNullOrEmpty(shipData.Metadata.PlayerId) ||
                    !shipData.Grids.Any())
                {
                    corrupted.Add(filePath);
                }
            }
            catch (Exception)
            {
                corrupted.Add(filePath);
            }
        }

        shell.WriteLine($"=== Ship Save Cleanup Analysis ===");
        shell.WriteLine($"Old files (>{olderThanDays} days): {toDelete.Count}");
        shell.WriteLine($"Corrupted files: {corrupted.Count}");
        shell.WriteLine($"Total files to remove: {toDelete.Count + corrupted.Count}");

        if (dryRun)
        {
            shell.WriteLine("DRY RUN - No files will be deleted");
            
            if (toDelete.Any())
            {
                shell.WriteLine("\nOld files that would be deleted:");
                foreach (var file in toDelete)
                {
                    shell.WriteLine($"  {Path.GetFileName(file)}");
                }
            }

            if (corrupted.Any())
            {
                shell.WriteLine("\nCorrupted files that would be deleted:");
                foreach (var file in corrupted)
                {
                    shell.WriteLine($"  {Path.GetFileName(file)}");
                }
            }
        }
        else
        {
            var deletedCount = 0;
            
            foreach (var file in toDelete.Concat(corrupted))
            {
                try
                {
                    File.Delete(file);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    shell.WriteLine($"Failed to delete {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            shell.WriteLine($"Successfully deleted {deletedCount} ship save files");
        }
    }
}