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
    public string Command => "shipsave_list";
    public string Description => "List blacklisted ship checksums (ships are stored client-side)";
    public string Help => "shipsave_list";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine("=== Ship Blacklist Status ===");
        shell.WriteLine("Note: Ship save files are stored on player clients, not the server.");
        shell.WriteLine("This command shows server-side blacklisted ships only.");
        shell.WriteLine("");
        
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
        
        shell.WriteLine("Use 'shipsave_validate_checksum <checksum>' to validate a specific ship checksum.");
        shell.WriteLine("Use 'shipsave_blacklist <checksum> <reason>' to blacklist a ship by checksum.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipSaveValidateChecksumCommand : IConsoleCommand
{
    public string Command => "shipsave_validate_checksum";
    public string Description => "Validate a ship checksum format and blacklist status";
    public string Help => "shipsave_validate_checksum <checksum>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_validate_checksum <checksum>");
            shell.WriteLine("Note: Ship files are stored client-side. This validates checksum format only.");
            return;
        }

        var checksum = args[0];
        
        shell.WriteLine($"=== Checksum Validation ===");
        shell.WriteLine($"Checksum: {checksum}");
        shell.WriteLine("");
        
        // Analyze checksum format
        if (checksum.StartsWith("S:"))
        {
            shell.WriteLine("✅ Format: Server-bound checksum");
            var parts = checksum.Split(':', 3);
            if (parts.Length >= 3)
            {
                shell.WriteLine($"   Server Binding: {parts[1]}");
                shell.WriteLine($"   Base Checksum: {parts[2]}");
            }
        }
        else if (checksum.Length == 64 && !checksum.Contains(":"))
        {
            shell.WriteLine("⚠️  Format: Legacy SHA256 checksum");
        }
        else if (checksum.Contains(":"))
        {
            shell.WriteLine("✅ Format: Enhanced checksum");
            if (checksum.Contains(":C") && checksum.Contains(":CM"))
            {
                shell.WriteLine("   Includes: Container and component data");
            }
        }
        else
        {
            shell.WriteLine("❓ Format: Unknown or invalid");
        }
        
        // Check blacklist status
        if (ShipBlacklistService.IsBlacklisted(checksum))
        {
            var reason = ShipBlacklistService.GetBlacklistReason(checksum);
            shell.WriteLine($"🚫 Status: BLACKLISTED");
            shell.WriteLine($"   Reason: {reason}");
        }
        else
        {
            shell.WriteLine("✅ Status: Not blacklisted");
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
/// Server-side ship blacklisting system for legal compliance with persistent storage
/// </summary>
public static class ShipBlacklistService
{
    private static readonly HashSet<string> BlacklistedChecksums = new();
    private static readonly Dictionary<string, string> BlacklistReasons = new();
    private static readonly object _lock = new();
    private static string? _blacklistFilePath;
    private static bool _initialized = false;
    
    private static void EnsureInitialized()
    {
        if (_initialized) return;
        
        lock (_lock)
        {
            if (_initialized) return;
            
            var resourceManager = IoCManager.Resolve<Robust.Shared.ContentPack.IResourceManager>();
            var userDataPath = resourceManager.UserData.ToString() ?? "";
            _blacklistFilePath = Path.Combine(userDataPath, "ship_blacklist.yml");
            
            LoadBlacklist();
            _initialized = true;
        }
    }
    
    public static bool IsBlacklisted(string checksum)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return BlacklistedChecksums.Contains(checksum);
        }
    }
    
    public static void AddToBlacklist(string checksum, string reason)
    {
        EnsureInitialized();
        lock (_lock)
        {
            BlacklistedChecksums.Add(checksum);
            BlacklistReasons[checksum] = reason;
            SaveBlacklist();
        }
    }
    
    public static void RemoveFromBlacklist(string checksum)
    {
        EnsureInitialized();
        lock (_lock)
        {
            BlacklistedChecksums.Remove(checksum);
            BlacklistReasons.Remove(checksum);
            SaveBlacklist();
        }
    }
    
    public static string? GetBlacklistReason(string checksum)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return BlacklistReasons.TryGetValue(checksum, out var reason) ? reason : null;
        }
    }
    
    public static IEnumerable<(string checksum, string reason)> GetAllBlacklisted()
    {
        EnsureInitialized();
        lock (_lock)
        {
            return BlacklistedChecksums.Select(c => (c, BlacklistReasons.GetValueOrDefault(c, "No reason provided"))).ToList();
        }
    }
    
    private static void LoadBlacklist()
    {
        try
        {
            if (!File.Exists(_blacklistFilePath))
                return;
                
            var yamlContent = File.ReadAllText(_blacklistFilePath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
                
            var blacklistData = deserializer.Deserialize<Dictionary<string, string>>(yamlContent);
            
            BlacklistedChecksums.Clear();
            BlacklistReasons.Clear();
            
            foreach (var (checksum, reason) in blacklistData)
            {
                BlacklistedChecksums.Add(checksum);
                BlacklistReasons[checksum] = reason;
            }
            
            var sawmill = Logger.GetSawmill("ship-blacklist");
            sawmill.Info($"Loaded {BlacklistedChecksums.Count} blacklisted ships from {_blacklistFilePath}");
        }
        catch (Exception ex)
        {
            var sawmill = Logger.GetSawmill("ship-blacklist");
            sawmill.Error($"Failed to load blacklist from {_blacklistFilePath}: {ex.Message}");
        }
    }
    
    private static void SaveBlacklist()
    {
        try
        {
            var blacklistData = BlacklistReasons.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
                
            var yamlContent = serializer.Serialize(blacklistData);
            File.WriteAllText(_blacklistFilePath!, yamlContent);
            
            var sawmill = Logger.GetSawmill("ship-blacklist");
            sawmill.Info($"Saved {BlacklistedChecksums.Count} blacklisted ships to {_blacklistFilePath}");
        }
        catch (Exception ex)
        {
            var sawmill = Logger.GetSawmill("ship-blacklist");
            sawmill.Error($"Failed to save blacklist to {_blacklistFilePath}: {ex.Message}");
        }
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