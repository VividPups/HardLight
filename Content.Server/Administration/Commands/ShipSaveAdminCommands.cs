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
                shell.WriteLine($"    Checksum: {shipData.Metadata.Checksum}");
                shell.WriteLine($"    File Size: {fileSize / 1024.0:F1} KB");
                shell.WriteLine($"    Entities: {shipData.Grids.Sum(g => g.Entities.Count)}");
                shell.WriteLine($"    Containers: {shipData.Grids.Sum(g => g.Entities.Count(e => e.IsContainer))}");
                shell.WriteLine($"    Contained Items: {shipData.Grids.Sum(g => g.Entities.Count(e => e.IsContained))}");
                
                // Show blacklist status
                if (ShipBlacklistService.IsBlacklisted(shipData.Metadata.Checksum))
                {
                    var reason = ShipBlacklistService.GetBlacklistReason(shipData.Metadata.Checksum);
                    shell.WriteLine($"    🚫 BLACKLISTED: {reason}");
                }
                
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

// ===== NEW BLACKLISTING SYSTEM =====

/// <summary>
/// Server-side ship blacklisting system for legal compliance
/// </summary>
public static class ShipBlacklistService
{
    private static readonly HashSet<string> BlacklistedChecksums = new();
    private static readonly Dictionary<string, string> BlacklistReasons = new();
    
    public static bool IsBlacklisted(string checksum)
    {
        return BlacklistedChecksums.Contains(checksum);
    }
    
    public static void AddToBlacklist(string checksum, string reason)
    {
        BlacklistedChecksums.Add(checksum);
        BlacklistReasons[checksum] = reason;
    }
    
    public static void RemoveFromBlacklist(string checksum)
    {
        BlacklistedChecksums.Remove(checksum);
        BlacklistReasons.Remove(checksum);
    }
    
    public static string? GetBlacklistReason(string checksum)
    {
        return BlacklistReasons.TryGetValue(checksum, out var reason) ? reason : null;
    }
    
    public static IEnumerable<(string checksum, string reason)> GetAllBlacklisted()
    {
        return BlacklistedChecksums.Select(c => (c, BlacklistReasons.GetValueOrDefault(c, "No reason provided")));
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipBlacklistCommand : IConsoleCommand
{
    public string Command => "shipsave_blacklist";
    public string Description => "Add a ship to the server blacklist (prevents loading)";
    public string Help => "shipsave_blacklist <filename_or_checksum> [reason]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_blacklist <filename_or_checksum> [reason]");
            shell.WriteLine("Examples:");
            shell.WriteLine("  shipsave_blacklist suspicious_ship.yml \"Dev environment exploit\"");
            shell.WriteLine("  shipsave_blacklist S:a1b2c3d4:G1T50... \"Manual checksum blacklist\"");
            return;
        }

        var input = args[0];
        var reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Blacklisted by admin";
        string checksum;

        // Check if input looks like a filename
        if (input.EndsWith(".yml") && !input.Contains(":"))
        {
            // Try to find and read the file to get its checksum
            var resourceManager = IoCManager.Resolve<Robust.Shared.ContentPack.IResourceManager>();
            var userDataPath = resourceManager.UserData.ToString() ?? "";
            var savedShipsPath = Path.Combine(userDataPath, "saved_ships");
            var filePath = Path.Combine(savedShipsPath, input);

            if (!File.Exists(filePath))
            {
                shell.WriteLine($"❌ Ship file '{input}' not found.");
                return;
            }

            try
            {
                var yamlContent = File.ReadAllText(filePath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var shipData = deserializer.Deserialize<ShipGridData>(yamlContent);
                checksum = shipData.Metadata.Checksum;
                
                shell.WriteLine($"📄 Found ship: {shipData.Metadata.ShipName}");
                shell.WriteLine($"👤 Player: {shipData.Metadata.PlayerId}");
            }
            catch (Exception ex)
            {
                shell.WriteLine($"❌ Failed to read ship file: {ex.Message}");
                return;
            }
        }
        else
        {
            // Assume it's a checksum
            checksum = input;
        }

        ShipBlacklistService.AddToBlacklist(checksum, reason);
        
        var shortChecksum = checksum.Length > 30 ? checksum.Substring(0, 30) + "..." : checksum;
        shell.WriteLine($"✅ Ship blacklisted: {shortChecksum}");
        shell.WriteLine($"📝 Reason: {reason}");
        shell.WriteLine($"🛡️ This ship will no longer load on the server");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipUnblacklistCommand : IConsoleCommand
{
    public string Command => "shipsave_unblacklist";
    public string Description => "Remove a ship from the server blacklist";
    public string Help => "shipsave_unblacklist <filename_or_checksum>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_unblacklist <filename_or_checksum>");
            shell.WriteLine("Examples:");
            shell.WriteLine("  shipsave_unblacklist suspicious_ship.yml");
            shell.WriteLine("  shipsave_unblacklist S:a1b2c3d4:G1T50...");
            return;
        }

        var input = args[0];
        string checksum;

        // Check if input looks like a filename
        if (input.EndsWith(".yml") && !input.Contains(":"))
        {
            // Try to find and read the file to get its checksum
            var resourceManager = IoCManager.Resolve<Robust.Shared.ContentPack.IResourceManager>();
            var userDataPath = resourceManager.UserData.ToString() ?? "";
            var savedShipsPath = Path.Combine(userDataPath, "saved_ships");
            var filePath = Path.Combine(savedShipsPath, input);

            if (!File.Exists(filePath))
            {
                shell.WriteLine($"❌ Ship file '{input}' not found.");
                return;
            }

            try
            {
                var yamlContent = File.ReadAllText(filePath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var shipData = deserializer.Deserialize<ShipGridData>(yamlContent);
                checksum = shipData.Metadata.Checksum;
            }
            catch (Exception ex)
            {
                shell.WriteLine($"❌ Failed to read ship file: {ex.Message}");
                return;
            }
        }
        else
        {
            // Assume it's a checksum
            checksum = input;
        }
        
        if (!ShipBlacklistService.IsBlacklisted(checksum))
        {
            var shortChecksum = checksum.Length > 30 ? checksum.Substring(0, 30) + "..." : checksum;
            shell.WriteLine($"❌ Ship not found in blacklist: {shortChecksum}");
            return;
        }

        ShipBlacklistService.RemoveFromBlacklist(checksum);
        var shortChecksumResult = checksum.Length > 30 ? checksum.Substring(0, 30) + "..." : checksum;
        shell.WriteLine($"✅ Ship removed from blacklist: {shortChecksumResult}");
        shell.WriteLine($"🔓 This ship can now load on the server again");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipBlacklistListCommand : IConsoleCommand
{
    public string Command => "shipsave_blacklist_list";
    public string Description => "List all blacklisted ship checksums";
    public string Help => "shipsave_blacklist_list";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var blacklisted = ShipBlacklistService.GetAllBlacklisted().ToList();
        
        if (!blacklisted.Any())
        {
            shell.WriteLine("📋 No ships are currently blacklisted.");
            return;
        }

        shell.WriteLine($"📋 Blacklisted Ships ({blacklisted.Count}):");
        shell.WriteLine("");
        
        foreach (var (checksum, reason) in blacklisted)
        {
            var shortChecksum = checksum.Length > 30 ? checksum.Substring(0, 30) + "..." : checksum;
            shell.WriteLine($"🚫 {shortChecksum}");
            shell.WriteLine($"   Reason: {reason}");
            shell.WriteLine("");
        }
    }
}