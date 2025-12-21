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
    private Dictionary<string, string> _coverCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _heroCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _descriptionCache = new(StringComparer.OrdinalIgnoreCase);

    public MetadataService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "Data");
        Directory.CreateDirectory(folder);
        _coverCachePath = Path.Combine(folder, "cover_cache.json");
        _heroCachePath = Path.Combine(folder, "hero_cache.json");
        _descriptionCachePath = Path.Combine(folder, "description_cache.json");
        LoadCaches();
    }

    private void LoadCaches()
    {
        if (File.Exists(_coverCachePath))
        {
            try
            {
                var json = File.ReadAllText(_coverCachePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null) _coverCache = data;
            }
            catch { }
        }

        if (File.Exists(_heroCachePath))
        {
            try
            {
                var json = File.ReadAllText(_heroCachePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null) _heroCache = data;
            }
            catch { }
        }

        if (File.Exists(_descriptionCachePath))
        {
            try
            {
                var json = File.ReadAllText(_descriptionCachePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null) _descriptionCache = data;
            }
            catch { }
        }
    }

    public void SaveCoverCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_coverCache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_coverCachePath, json);
        }
        catch { }
    }

    public void SaveHeroCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_heroCache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_heroCachePath, json);
        }
        catch { }
    }

    public void SaveDescriptionCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_descriptionCache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_descriptionCachePath, json);
        }
        catch { }
    }

    public string? GetCover(string gameId)
    {
        _coverCache.TryGetValue(gameId, out var url);
        return url;
    }

    public void SetCover(string gameId, string url)
    {
        _coverCache[gameId] = url;
        SaveCoverCache();
    }

    public bool HasCover(string gameId)
    {
        return _coverCache.ContainsKey(gameId);
    }

    public string? GetHero(string gameId)
    {
        _heroCache.TryGetValue(gameId, out var url);
        return url;
    }

    public void SetHero(string gameId, string url)
    {
        _heroCache[gameId] = url;
        SaveHeroCache();
    }

    public bool HasHero(string gameId)
    {
        return _heroCache.ContainsKey(gameId);
    }

    public string? GetDescription(string gameId)
    {
        _descriptionCache.TryGetValue(gameId, out var desc);
        return desc;
    }

    public void SetDescription(string gameId, string desc)
    {
        _descriptionCache[gameId] = desc;
        SaveDescriptionCache();
    }

    public bool HasDescription(string gameId)
    {
        return _descriptionCache.ContainsKey(gameId);
    }
}
