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
using Content.Server.Commands;

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
    public string Description => "Validate ship save file integrity and checksums (ships are client-side)";
    public string Help => "shipsave_validate <filename> - NOTE: Ships stored client-side only";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_validate <filename>");
            shell.WriteLine("NOTE: Ships are stored CLIENT-SIDE only in player's export folder");
            shell.WriteLine("This command cannot access player ship files directly");
            shell.WriteLine("Use 'shipsave_validate_checksum <checksum>' instead");
            return;
        }

        // First try to parse as player:filename format
        var parts = args[0].Split(':', 2);
        if (parts.Length == 2)
        {
            var playerIdentifier = parts[0];
            var filename = parts[1];
            var performer = shell.Player ?? throw new InvalidOperationException("Shell must have a player");
            
            if (!CommandUtils.TryGetSessionByUsernameOrId(shell, playerIdentifier, performer, out var session))
            {
                return;
            }

            var adminName = performer.Name;
            var key = $"ship_data_{adminName}_{filename}";
            
            // Register callback for ship data response
            Content.Server.Shuttles.Save.ShipSaveSystem.RegisterAdminRequest(key, yamlData => 
            {
                try
                {
                    var shipSerializationSystem = IoCManager.Resolve<ShipSerializationSystem>();
                    var validatedData = shipSerializationSystem.DeserializeShipGridDataFromYaml(yamlData, session.UserId, out var wasConverted);
                    
                    shell.WriteLine($"=== Validation Results for {filename} ===");
                    shell.WriteLine($"YAML parsing: SUCCESS");
                    shell.WriteLine($"Checksum validation: SUCCESS");
                    if (wasConverted)
                    {
                        shell.WriteLine($"Legacy format detected and converted");
                    }
                    shell.WriteLine($"Structure validation: SUCCESS");
                    shell.WriteLine($"Ship: {validatedData.Metadata.ShipName}");
                    shell.WriteLine($"Entities: {validatedData.Grids[0].Entities.Count}");
                    shell.WriteLine($"Tiles: {validatedData.Grids[0].Tiles.Count}");
                }
                catch (Exception ex)
                {
                    shell.WriteLine($"VALIDATION FAILED: {ex.Message}");
                }
            });

            // Audit log
            Robust.Shared.Log.Logger.InfoS("admin.ship", $"ADMIN ACTION: {adminName} requested ship validation from player {session.Name} for file {filename}");
            
            // Send request to player's client
            var shipSaveSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<Content.Server.Shuttles.Save.ShipSaveSystem>();
            shipSaveSystem.SendAdminRequestShipData(filename, adminName, session);
            
            shell.WriteLine($"Requesting ship data from {session.Name} for file: {filename}");
            shell.WriteLine("(Validation results will appear when client responds)");
        }
        else
        {
            shell.WriteLine("Usage: shipsave_validate <player:filename>");
            shell.WriteLine("Example: shipsave_validate john:MyShip_20250816_143022.yml");
            shell.WriteLine("Requests ship file from player's client for validation");
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
    public string Description => "Add a ship to the server blacklist by checksum (prevents loading)";
    public string Help => "shipsave_blacklist <checksum> [reason]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_blacklist <checksum> [reason]");
            shell.WriteLine("Examples:");
            shell.WriteLine("  shipsave_blacklist S:a1b2c3d4:G1T50... \"Manual checksum blacklist\"");
            shell.WriteLine("");
            shell.WriteLine("NOTE: Ships are stored client-side only.");
            shell.WriteLine("Use checksums from ship loading attempts or 'shipsave_validate_checksum'");
            return;
        }

        var checksum = args[0];
        var reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Blacklisted by admin";

        // Validate checksum format
        if (checksum.Length < 10 || (!checksum.Contains(":") && checksum.Length != 64))
        {
            shell.WriteLine("Invalid checksum format. Checksums should be:");
            shell.WriteLine("  - Enhanced format: G1:T50[types]:E10[prototypes]:P1234");
            shell.WriteLine("  - Server-bound: S:binding:enhanced_checksum");
            shell.WriteLine("  - Legacy SHA256: 64 character hex string");
            return;
        }

        ShipBlacklistService.AddToBlacklist(checksum, reason);
        
        // Audit log
        var adminName = (shell.Player?.Name ?? "Console");
        Robust.Shared.Log.Logger.InfoS("admin.ship", $"ADMIN ACTION: {adminName} blacklisted ship checksum {checksum} - Reason: {reason}");
        
        var shortChecksum = checksum.Length > 30 ? checksum.Substring(0, 30) + "..." : checksum;
        shell.WriteLine($"Ship blacklisted: {shortChecksum}");
        shell.WriteLine($"Reason: {reason}");
        shell.WriteLine($"This ship will no longer load on the server");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipUnblacklistCommand : IConsoleCommand
{
    public string Command => "shipsave_unblacklist";
    public string Description => "Remove a ship from the server blacklist by checksum";
    public string Help => "shipsave_unblacklist <checksum>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_unblacklist <checksum>");
            shell.WriteLine("Examples:");
            shell.WriteLine("  shipsave_unblacklist S:a1b2c3d4:G1T50...");
            shell.WriteLine("");
            shell.WriteLine("NOTE: Ships are stored client-side only.");
            shell.WriteLine("Use checksums from 'shipsave_blacklist_list' or ship loading attempts");
            return;
        }

        var checksum = args[0];
        
        if (!ShipBlacklistService.IsBlacklisted(checksum))
        {
            var shortChecksum = checksum.Length > 30 ? checksum.Substring(0, 30) + "..." : checksum;
            shell.WriteLine($"Ship not found in blacklist: {shortChecksum}");
            return;
        }

        ShipBlacklistService.RemoveFromBlacklist(checksum);
        
        // Audit log
        var adminName = (shell.Player?.Name ?? "Console");
        Robust.Shared.Log.Logger.InfoS("admin.ship", $"ADMIN ACTION: {adminName} unblacklisted ship checksum {checksum}");
        
        var shortChecksumResult = checksum.Length > 30 ? checksum.Substring(0, 30) + "..." : checksum;
        shell.WriteLine($"Ship removed from blacklist: {shortChecksumResult}");
        shell.WriteLine($"This ship can now load on the server again");
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

[AdminCommand(AdminFlags.Admin)]
public sealed class ShipSavePlayerListCommand : IConsoleCommand
{
    public string Command => "shipsave_player_list";
    public string Description => "List saved ships for a specific player (requests from client)";
    public string Help => "shipsave_player_list <username_or_id>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine("Usage: shipsave_player_list <username_or_id>");
            shell.WriteLine("Requests ship list from the specified player's client");
            return;
        }

        var playerIdentifier = args[0];
        var performer = shell.Player ?? throw new InvalidOperationException("Shell must have a player");
        
        if (!CommandUtils.TryGetSessionByUsernameOrId(shell, playerIdentifier, performer, out var session))
        {
            return;
        }

        var adminName = performer.Name;
        var key = $"player_ships_{adminName}";
        
        // Register callback for response
        Content.Server.Shuttles.Save.ShipSaveSystem.RegisterAdminRequest(key, result => 
        {
            shell.WriteLine($"=== Ships for {session.Name} ===");
            shell.WriteLine(result);
        });

        // Audit log
        Robust.Shared.Log.Logger.InfoS("admin.ship", $"ADMIN ACTION: {adminName} requested ship list from player {session.Name} ({session.UserId})");
        
        // Send request to the target player's client
        var shipSaveSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<Content.Server.Shuttles.Save.ShipSaveSystem>();
        shipSaveSystem.SendAdminRequestPlayerShips(session.UserId, adminName, session);
        
        shell.WriteLine($"Requesting ship list from {session.Name}...");
        shell.WriteLine("(Response will appear when client responds)");
    }
}