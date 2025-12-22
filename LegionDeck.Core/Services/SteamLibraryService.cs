using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LegionDeck.Core.Models;

namespace LegionDeck.Core.Services;

public class SteamLibraryService
{
    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SteamLibraryService] {message}\n");
        }
        catch {{ }}
    }

    public async Task<List<SteamWishlistItem>> GetOwnedGamesAsync()
    {
        var ownedGames = new List<SteamWishlistItem>();
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var steamCookieFilePath = Path.Combine(authTokensPath, "steam_cookies.json");
        
        var userDataUrl = "https://store.steampowered.com/dynamicstore/userdata/";
        
        try 
        {
            var (userDataContent, _) = await SteamAuthService.FetchProtectedPageAsync(userDataUrl, steamCookieFilePath); 
            
            using var doc = JsonDocument.Parse(userDataContent);
            if (doc.RootElement.TryGetProperty("rgOwnedApps", out var rgOwnedApps))
            {
                foreach (var id in rgOwnedApps.EnumerateArray())
                {
                    var appId = id.GetInt32();
                    ownedGames.Add(new SteamWishlistItem 
                    { 
                        AppId = appId, 
                        Name = $"AppID {appId}" // Placeholder, will need enrichment
                    });
                }
                Log($"Found {ownedGames.Count} owned games via Dynamic Store API.");
            }
            else
            {
                Log("rgOwnedApps property missing in response.");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to fetch owned games: {ex.Message}");
        }
        
        return ownedGames;
    }
}
