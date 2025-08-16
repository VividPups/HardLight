using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Server.Shuttles;

/// <summary>
/// Service for generating unique server hardware identity for ship save security
/// </summary>
public sealed class ServerIdentityService
{
    private ISawmill _sawmill = default!;
    
    private string? _cachedHardwareId;
    private readonly object _lock = new();
    
    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("server-identity");
    }
    
    /// <summary>
    /// Gets a unique hardware-based identifier for this server instance
    /// </summary>
    public string GetServerHardwareId()
    {
        if (_cachedHardwareId != null)
            return _cachedHardwareId;
            
        lock (_lock)
        {
            if (_cachedHardwareId != null)
                return _cachedHardwareId;
                
            _cachedHardwareId = GenerateHardwareId();
            _sawmill.Info($"Generated server hardware ID: {_cachedHardwareId.Substring(0, 16)}...");
            return _cachedHardwareId;
        }
    }
    
    private string GenerateHardwareId()
    {
        var components = new List<string>();
        
        try
        {
            // Primary MAC address
            components.Add(GetPrimaryMacAddress());
            
            // Machine name as fallback
            components.Add(Environment.MachineName);
            
            // OS version for additional uniqueness
            components.Add(Environment.OSVersion.ToString());
            
            // Processor count and architecture
            components.Add($"{Environment.ProcessorCount}_{Environment.Is64BitOperatingSystem}");
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"Failed to collect some hardware components: {ex.Message}");
            // Fallback to basic identifiers
            components.Add(Environment.MachineName);
            components.Add(Environment.OSVersion.ToString());
            components.Add(DateTime.UtcNow.ToString("yyyy-MM-dd")); // Date-based fallback
        }
        
        // Remove empty components
        components = components.Where(c => !string.IsNullOrEmpty(c)).ToList();
        
        if (components.Count == 0)
        {
            throw new InvalidOperationException("Unable to generate server hardware ID - no valid components found");
        }
        
        var combined = string.Join("|", components);
        return ComputeSha256Hash(combined);
    }
    
    private string GetPrimaryMacAddress()
    {
        try
        {
            var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up && 
                                     ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
                                     
            return networkInterface?.GetPhysicalAddress().ToString() ?? "UNKNOWN";
        }
        catch
        {
            return "UNKNOWN";
        }
    }
    
    private static string ComputeSha256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

