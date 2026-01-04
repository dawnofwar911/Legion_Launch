using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using LegionDeck.Core.Models;

namespace LegionDeck.Core.Services;

public class BattleNetLibraryService
{
    private readonly ConfigService _configService;

    // Hardcoded mappings from Playnite/Official sources
    private static readonly Dictionary<string, string> GameIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "WoW", "World of Warcraft" },
        { "D3", "Diablo III" },
        { "S2", "StarCraft II" },
        { "S1", "StarCraft" },
        { "WTCG", "Hearthstone" },
        { "Hero", "Heroes of the Storm" },
        { "Pro", "Overwatch 2" },
        { "D2", "Diablo II" },
        { "VIPR", "Call of Duty: Black Ops 4" },
        { "ODIN", "Call of Duty: Modern Warfare" },
        { "W3", "Warcraft III: Reforged" },
        { "LAZR", "Call of Duty: MW 2 Remastered" },
        { "ZEUS", "Call of Duty: Black Ops Cold War" },
        { "WLBY", "Crash Bandicoot 4" },
        { "OSI", "Diablo II: Resurrected" },
        { "RTRO", "Blizzard Arcade Collection" },
        { "FORE", "Call of Duty: Vanguard" },
        { "ANBS", "Diablo Immortal" },
        { "AUKS", "Call of Duty: Modern Warfare II" },
        { "Fen", "Diablo IV" },
        { "D1", "Diablo" },
        { "W1R", "Warcraft: Remastered" },
        { "W2R", "Warcraft II: Remastered" },
        { "W1", "Warcraft: Orcs & Humans" },
        { "W2", "Warcraft II: BNE" },
        { "GRY", "Warcraft Rumble" }
    };

    private static readonly Dictionary<string, string> TitleIdToProductMap = new()
    {
        { "5730135", "WoW" },
        { "1465140039", "WTCG" }, 
        { "5272175", "Pro" },    
        { "17459", "D3" },       
        { "4613486", "Fen" },    
        { "1095647827", "ANBS" }, 
        { "21298", "S2" },       
        { "4674137", "GRY" },    
        { "1214607983", "Hero" }, 
        { "1146311730", "Destiny2" }, 
        { "1329875278", "ODIN" }, 
        { "1447645266", "VIPR" }, 
        { "1514493267", "ZEUS" }, 
        { "1179603525", "FORE" }, 
        { "1396920146", "SeaOfThieves" } 
    };

    public BattleNetLibraryService(ConfigService configService)
    {
        _configService = configService;
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [BattleNetLibraryService] {message}\n");
        }
        catch { }
    }

    public async Task<List<LocalLibraryService.InstalledGame>> GetOwnedGamesAsync()
    {
        var games = new List<LocalLibraryService.InstalledGame>();
        try
        {
            Log("Starting Battle.net Account Sync...");
            
            // First, try to refresh the session
            var authService = new BattleNetAuthService();
            var refreshed = await authService.RefreshSessionAsync();
            if (!refreshed)
            {
                Log("Battle.net session refresh failed. User needs to log in again.");
                throw new UnauthorizedAccessException("Battle.net session expired. Please log in again.");
            }

            // 1. Fetch Modern Games
            var (modernJson, _) = await BattleNetAuthService.FetchProtectedPageAsync($"{BattleNetAuthService.BaseUrl}/api/games-and-subs");
            if (!string.IsNullOrEmpty(modernJson) && modernJson.Trim().StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(modernJson);
                if (doc.RootElement.TryGetProperty("gameAccounts", out var accounts))
                {
                    var pageGames = new List<LocalLibraryService.InstalledGame>();
                    foreach (var acc in accounts.EnumerateArray())
                    {
                        string? apiName = null;
                        if (acc.TryGetProperty("localizedGameName", out var nameProp))
                        {
                            apiName = nameProp.ValueKind == JsonValueKind.Number ? nameProp.GetInt32().ToString() : nameProp.GetString();
                        }

                        string? id = null;
                        if (acc.TryGetProperty("titleId", out var idProp))
                        {
                            id = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString();
                        }

                        if (!string.IsNullOrEmpty(id))
                        {
                            var productCode = TitleIdToProductMap.TryGetValue(id, out var p) ? p : id;
                            var name = GameIdMap.TryGetValue(productCode, out var mappedName) ? mappedName : apiName;
                            
                            if (!string.IsNullOrEmpty(name))
                            {
                                pageGames.Add(new LocalLibraryService.InstalledGame
                                {
                                    Id = productCode,
                                    Name = name,
                                    Source = "Battle.net",
                                    IsInstalled = false
                                });
                            }
                        }
                    }
                    // Deduplicate by Name
                    pageGames = pageGames.GroupBy(g => g.Id)
                        .Select(group => group.OrderBy(g => g.Name.Length).First()) // Keep shortest name for a given ID
                        .ToList();

                    games.AddRange(pageGames);
                }
            }

            // 2. Fetch Classic Games
            var (classicJson, _) = await BattleNetAuthService.FetchProtectedPageAsync($"{BattleNetAuthService.BaseUrl}/api/classic-games");
            if (!string.IsNullOrEmpty(classicJson) && classicJson.Trim().StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(classicJson);
                if (doc.RootElement.TryGetProperty("classicGames", out var classics))
                {
                    foreach (var cg in classics.EnumerateArray())
                    {
                        string? name = null;
                        if (cg.TryGetProperty("localizedGameName", out var nameProp))
                        {
                            name = nameProp.ValueKind == JsonValueKind.Number ? nameProp.GetInt32().ToString() : nameProp.GetString();
                        }

                        if (!string.IsNullOrEmpty(name))
                        {
                            games.Add(new LocalLibraryService.InstalledGame
                            {
                                Id = name,
                                Name = name,
                                Source = "Battle.net",
                                IsInstalled = false
                            });
                        }
                    }
                }
            }

            Log($"Battle.net Account Sync finished. Found {games.Count} games.");
        }
        catch (Exception ex)
        {
            if (ex is UnauthorizedAccessException) throw;
            Log($"Error during Battle.net Account Sync: {ex.Message}");
        }
        return games;
    }

    public async Task<List<LocalLibraryService.InstalledGame>> GetInstalledGamesAsync()
    {
        return await Task.Run(() => GetInstalledGames());
    }

    public List<LocalLibraryService.InstalledGame> GetInstalledGames()
    {
        var games = new List<LocalLibraryService.InstalledGame>();
        try
        {
            Log("Starting Battle.net game scan...");
            games.AddRange(ScanRegistry());
            Log($"Battle.net scan finished. Found {games.Count} games.");
        }
        catch (Exception ex)
        {
            Log($"Error scanning Battle.net games: {ex.Message}");
        }
        return games;
    }

    private List<LocalLibraryService.InstalledGame> ScanRegistry()
    {
        var games = new List<LocalLibraryService.InstalledGame>();
        var uninstallKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(uninstallKey);
            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var displayName = subKey.GetValue("DisplayName") as string;
                        var publisher = subKey.GetValue("Publisher") as string;
                        var installLocation = subKey.GetValue("InstallLocation") as string;
                        var uninstallString = subKey.GetValue("UninstallString") as string;

                        if (!string.IsNullOrEmpty(displayName) && 
                            (publisher == "Blizzard Entertainment" || (uninstallString != null && uninstallString.Contains("Battle.net"))))
                        {
                            if (displayName == "Battle.net") continue; 

                            string? productId = null;
                            if (!string.IsNullOrEmpty(uninstallString) && uninstallString.Contains("os-product-uninstall="))
                            {
                                var parts = uninstallString.Split(new[] { "os-product-uninstall=" }, StringSplitOptions.None);
                                if (parts.Length > 1)
                                {
                                    productId = parts[1].Split(' ')[0].Trim('"');
                                }
                            }

                            if (productId != null)
                            {
                                games.Add(new LocalLibraryService.InstalledGame
                                {
                                    Id = productId,
                                    Name = displayName,
                                    Source = "Battle.net",
                                    InstallPath = installLocation ?? string.Empty,
                                    LaunchUri = $"battlenet://{productId}"
                                });
                            }
                        }
                    }
                    catch {{ }}
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Registry scan failed: {ex.Message}");
        }
        return games;
    }
}