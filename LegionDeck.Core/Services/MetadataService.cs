using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class MetadataService
{
    private readonly string _coverCachePath;
    private readonly string _heroCachePath;
    private readonly string _descriptionCachePath;
    private readonly string _nameCachePath;
    private readonly string _typeCachePath;
    private readonly string _hiddenCachePath;
    private Dictionary<string, string> _coverCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _heroCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _descriptionCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _nameCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _typeCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, bool> _hiddenCache = new(StringComparer.OrdinalIgnoreCase);

    public MetadataService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "Metadata");
        Directory.CreateDirectory(folder);
        _coverCachePath = Path.Combine(folder, "cover_cache.json");
        _heroCachePath = Path.Combine(folder, "hero_cache.json");
        _descriptionCachePath = Path.Combine(folder, "description_cache.json");
        _nameCachePath = Path.Combine(folder, "name_cache.json");
        _typeCachePath = Path.Combine(folder, "type_cache.json");
        _hiddenCachePath = Path.Combine(folder, "hidden_cache.json");
        LoadCaches();
    }

    private void LoadCaches()
    {
        if (File.Exists(_coverCachePath))
        {
            try { var json = File.ReadAllText(_coverCachePath); var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json); if (data != null) _coverCache = data; } catch { }
        }
        if (File.Exists(_heroCachePath))
        {
            try { var json = File.ReadAllText(_heroCachePath); var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json); if (data != null) _heroCache = data; } catch { }
        }
        if (File.Exists(_descriptionCachePath))
        {
            try { var json = File.ReadAllText(_descriptionCachePath); var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json); if (data != null) _descriptionCache = data; } catch { }
        }
        if (File.Exists(_nameCachePath))
        {
            try { var json = File.ReadAllText(_nameCachePath); var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json); if (data != null) _nameCache = data; } catch { }
        }
        if (File.Exists(_typeCachePath))
        {
            try { var json = File.ReadAllText(_typeCachePath); var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json); if (data != null) _typeCache = data; } catch { }
        }
        if (File.Exists(_hiddenCachePath))
        {
            try { var json = File.ReadAllText(_hiddenCachePath); var data = JsonSerializer.Deserialize<Dictionary<string, bool>>(json); if (data != null) _hiddenCache = data; } catch { }
        }
    }

    public void SaveCoverCache()
    {
        try { var json = JsonSerializer.Serialize(_coverCache, new JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(_coverCachePath, json); } catch { }
    }
    public void SaveHeroCache()
    {
        try { var json = JsonSerializer.Serialize(_heroCache, new JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(_heroCachePath, json); } catch { }
    }
    public void SaveDescriptionCache()
    {
        try { var json = JsonSerializer.Serialize(_descriptionCache, new JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(_descriptionCachePath, json); } catch { }
    }
    public void SaveNameCache()
    {
        try { var json = JsonSerializer.Serialize(_nameCache, new JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(_nameCachePath, json); } catch { }
    }
    public void SaveTypeCache()
    {
        try { var json = JsonSerializer.Serialize(_typeCache, new JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(_typeCachePath, json); } catch { }
    }
    public void SaveHiddenCache()
    {
        try { var json = JsonSerializer.Serialize(_hiddenCache, new JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(_hiddenCachePath, json); } catch { }
    }

    public string? GetCover(string gameId) { _coverCache.TryGetValue(gameId, out var url); return url; }
    public void SetCover(string gameId, string url) { _coverCache[gameId] = url; SaveCoverCache(); }
    public bool HasCover(string gameId) => _coverCache.ContainsKey(gameId);

    public string? GetHero(string gameId) { _heroCache.TryGetValue(gameId, out var url); return url; }
    public void SetHero(string gameId, string url) { _heroCache[gameId] = url; SaveHeroCache(); }
    public bool HasHero(string gameId) => _heroCache.ContainsKey(gameId);

    public string? GetDescription(string gameId) { _descriptionCache.TryGetValue(gameId, out var desc); return desc; }
    public void SetDescription(string gameId, string desc) { _descriptionCache[gameId] = desc; SaveDescriptionCache(); }
    public bool HasDescription(string gameId) => _descriptionCache.ContainsKey(gameId);

    public string? GetName(string gameId) { _nameCache.TryGetValue(gameId, out var name); return name; }
    public void SetName(string gameId, string name) { _nameCache[gameId] = name; SaveNameCache(); }
    public bool HasName(string gameId) => _nameCache.ContainsKey(gameId);

    public string? GetType(string gameId) { _typeCache.TryGetValue(gameId, out var type); return type; }
    public void SetType(string gameId, string type) { _typeCache[gameId] = type; SaveTypeCache(); }
    public bool HasType(string gameId) => _typeCache.ContainsKey(gameId);

    public void SetHidden(string gameId, bool isHidden) { _hiddenCache[gameId] = isHidden; SaveHiddenCache(); }
    public bool IsHidden(string gameId) { _hiddenCache.TryGetValue(gameId, out var isHidden); return isHidden; }
}