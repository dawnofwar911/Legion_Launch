using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LegionDeck.CLI.Services;

public class ConfigService
{
    private readonly string _configFilePath;
    private Dictionary<string, string> _apiKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ConfigService()
    {
        var configFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "Config");
        Directory.CreateDirectory(configFolderPath); // Ensure directory exists
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
                var loadedKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loadedKeys != null)
                {
                    _apiKeys = loadedKeys;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to load config from {_configFilePath}: {ex.Message}");
                _apiKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Reset to empty to prevent further errors
            }
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_apiKeys, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to save config to {_configFilePath}: {ex.Message}");
        }
    }

    public void SetApiKey(string service, string key)
    {
        if (string.IsNullOrEmpty(service)) throw new ArgumentNullException(nameof(service));
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        _apiKeys[service] = key;
        SaveConfig();
        Console.WriteLine($"API key for '{service}' saved successfully.");
    }

    public string? GetApiKey(string service)
    {
        if (string.IsNullOrEmpty(service)) throw new ArgumentNullException(nameof(service));
        _apiKeys.TryGetValue(service, out var key);
        return key;
    }
}
