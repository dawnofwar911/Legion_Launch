using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class MetadataService
{
    private readonly string _filePath;
    private Dictionary<string, string> _coverCache = new(StringComparer.OrdinalIgnoreCase);

    public MetadataService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "Data");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "cover_cache.json");
        LoadCache();
    }

    private void LoadCache()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null) _coverCache = data;
            }
            catch { }
        }
    }

    public void SaveCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_coverCache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
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
        SaveCache();
    }

    public bool HasCover(string gameId)
    {
        return _coverCache.ContainsKey(gameId);
    }
}
