using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Content.Shared._NF.Shipyard.Components;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// Handles persistent caching of purchased ship data across rounds and restarts.
/// </summary>
public static class ShipyardPersistentCache
{
    private static readonly string CacheFilePath = Path.Combine("Data", "shipyard_cache.json");

    public class CachedShipData
    {
        public Guid DeedId { get; set; }
        public string? Owner { get; set; }
        public string? ShipName { get; set; }
        public string? ShipNameSuffix { get; set; }
        public bool PurchasedWithVoucher { get; set; }
        public DateTime PurchaseDate { get; set; }
        public int AppraisedValue { get; set; } // Value at time of purchase for resale
        // Add more fields as needed
    }

    private static Dictionary<Guid, CachedShipData> _cache = new();

    public static IReadOnlyDictionary<Guid, CachedShipData> Cache => _cache;

    public static void AddOrUpdate(CachedShipData data)
    {
        _cache[data.DeedId] = data;
        Save();
    }

    public static bool TryGet(Guid deedId, out CachedShipData? data)
    {
        return _cache.TryGetValue(deedId, out data);
    }

    public static void Remove(Guid deedId)
    {
        if (_cache.Remove(deedId))
            Save();
    }

    public static void Load()
    {
        if (!File.Exists(CacheFilePath))
        {
            _cache = new();
            return;
        }
        var json = File.ReadAllText(CacheFilePath);
        _cache = JsonSerializer.Deserialize<Dictionary<Guid, CachedShipData>>(json) ?? new();
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CacheFilePath, json);
    }
}
