using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LegionDeck.Core.Models;

namespace LegionDeck.Core.Services;

public class LibraryCacheService
{
    private readonly string _cacheFolder;

    public LibraryCacheService()
    {
        _cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "Cache");
        Directory.CreateDirectory(_cacheFolder);
    }

    public async Task SaveLibraryAsync(string source, List<LocalLibraryService.InstalledGame> games)
    {
        try
        {
            var path = Path.Combine(_cacheFolder, $"{source.ToLower()}_library.json");
            var json = JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex) { Log($"Error saving {source} cache: {ex.Message}"); }
    }

    public async Task<List<LocalLibraryService.InstalledGame>> LoadLibraryAsync(string source)
    {
        try
        {
            var path = Path.Combine(_cacheFolder, $"{source.ToLower()}_library.json");
            if (!File.Exists(path)) return new List<LocalLibraryService.InstalledGame>();

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<LocalLibraryService.InstalledGame>>(json) ?? new List<LocalLibraryService.InstalledGame>();
        }
        catch { return new List<LocalLibraryService.InstalledGame>(); }
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LibraryCacheService] {message}\n");
        }
        catch { }
    }
}
