using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class GameProfile
{
    public int PowerMode { get; set; } = 2; // 1=Quiet, 2=Balanced, 3=Performance
    public string? Resolution { get; set; } // e.g. "1280,800"
    public int? RefreshRate { get; set; } // e.g. 60
}

public class AppConfig
{
    public Dictionary<string, string> ApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, GameProfile> GameProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int DefaultPowerMode { get; set; } = 2;
}

public class ConfigService
{
    private readonly string _configFilePath;
    private AppConfig _config = new();

    public ConfigService()
    {
        var configFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "Config");
        Directory.CreateDirectory(configFolderPath);
        _configFilePath = Path.Combine(configFolderPath, "app_config.json");
        LoadConfig();
    }

    private void LoadConfig()
    {
        if (File.Exists(_configFilePath))
        {
            try
            {
                var json = File.ReadAllText(_configFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null)
                {
                    _config = loaded;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to load config: {ex.Message}");
            }
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to save config: {ex.Message}");
        }
    }

    public void SetApiKey(string service, string key)
    {
        _config.ApiKeys[service] = key;
        SaveConfig();
    }

    public string? GetApiKey(string service)
    {
        _config.ApiKeys.TryGetValue(service, out var key);
        return key;
    }

    public GameProfile GetProfile(string gameId)
    {
        if (_config.GameProfiles.TryGetValue(gameId, out var profile))
            return profile;
        return new GameProfile();
    }

    public void SetProfile(string gameId, GameProfile profile)
    {
        _config.GameProfiles[gameId] = profile;
        SaveConfig();
    }

    public int DefaultPowerMode 
    { 
        get => _config.DefaultPowerMode;
        set { _config.DefaultPowerMode = value; SaveConfig(); }
    }
}
