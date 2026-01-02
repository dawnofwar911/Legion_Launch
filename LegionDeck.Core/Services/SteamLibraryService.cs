using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LegionDeck.Core.Models;

namespace LegionDeck.Core.Services;

public class SteamLibraryService
{
    private readonly ConfigService _configService;

    public SteamLibraryService(ConfigService configService)
    {
        _configService = configService;
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SteamLibraryService] {message}\n");
        }
        catch {{ }}
    }

    private static System.Collections.Generic.Dictionary<int, string>? _masterAppListCache = null;

    public async Task<List<SteamWishlistItem>> GetOwnedGamesAsync()
    {
        var ownedGames = new List<SteamWishlistItem>();
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var steamCookieFilePath = Path.Combine(authTokensPath, "steam_cookies.json");
        
        string? apiKey = _configService.GetApiKey("STEAM");

        // 1. Ensure Master App List is loaded (for instant names)
        if (_masterAppListCache == null)
        {
            Log("Fetching master Steam app list for name resolution...");
            var storeService = new SteamStoreService();
            // Pass API Key if available to use reliable IStoreService
            _masterAppListCache = await storeService.GetFullAppListAsync(apiKey);
            Log($"Master app list loaded with {_masterAppListCache.Count} items.");
        }

        long steamId64 = 0;
        // Try to get SteamID64 from Cookies
        try 
        {
            if (File.Exists(steamCookieFilePath))
            {
                var cookieJson = File.ReadAllText(steamCookieFilePath);
                using var cookieDoc = JsonDocument.Parse(cookieJson);
                foreach (var cookie in cookieDoc.RootElement.EnumerateArray())
                {
                    if (cookie.TryGetProperty("Name", out var name) && name.GetString() == "steamLoginSecure")
                    {
                        var value = cookie.GetProperty("Value").GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            var idPart = value;
                            if (value.Contains("%7C")) idPart = value.Split("%7C")[0];
                            else if (value.Contains("|")) idPart = value.Split("|")[0];
                            if (long.TryParse(idPart, out var parsedId)) steamId64 = parsedId;
                        }
                        break;
                    }
                }
            }
        } catch {}

                // 3. Fallback: Dynamic Store API (userdata) - Only if XML failed
                // NEW STRATEGY: Use Configured API Key or Scrape webapi_token for IPlayerService
                
                string? webApiToken = null;
        
                if (!string.IsNullOrEmpty(apiKey))
                {
                    Log("Using configured Steam API Key.");
                }
                else
                {
                    Log("No Steam API Key configured. Attempting to scrape webapi_token...");
                    var storePageUrl = "https://store.steampowered.com/points/shop"; 
                    try
                    {
                        var (pageContent, _) = await SteamAuthService.FetchProtectedPageAsync(storePageUrl, steamCookieFilePath);
                        var tokenMatch = System.Text.RegularExpressions.Regex.Match(pageContent, "\"webapi_token\":\"([^\"]+)\"");
                        if (tokenMatch.Success)
                        {
                            webApiToken = tokenMatch.Groups[1].Value;
                            Log("Successfully scraped webapi_token.");
                        }
                    }
                    catch (Exception ex) { Log($"Failed to scrape token: {ex.Message}"); }
                }
        
                string? keyToUse = !string.IsNullOrEmpty(apiKey) ? apiKey : webApiToken;
        
                if (!string.IsNullOrEmpty(keyToUse) && steamId64 > 0)
                {
                    try 
                    {
                        // Note: 'key' param works for API Key. 'access_token' works for scraped token.
                        // We'll prioritize 'key' param if we have an API Key, otherwise 'access_token'.
                        
                        string apiUrl;
                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            apiUrl = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={apiKey}&steamid={steamId64}&include_appinfo=1&include_played_free_games=1&format=json";
                        }
                        else
                        {
                            apiUrl = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?access_token={webApiToken}&steamid={steamId64}&include_appinfo=1&include_played_free_games=1&format=json";
                        }
        
                        Log($"Calling GetOwnedGames with key/token for SteamID: {steamId64}");
                        
                        using var client = new System.Net.Http.HttpClient();
                        var json = await client.GetStringAsync(apiUrl);
                        
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("response", out var response) && 
                            response.TryGetProperty("games", out var games))
                        {
                            foreach (var game in games.EnumerateArray())
                            {
                                var appId = game.GetProperty("appid").GetInt32();
                                var name = game.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : $"AppID {appId}";
                                
                                ownedGames.Add(new SteamWishlistItem 
                                { 
                                    AppId = appId, 
                                    Name = name ?? $"AppID {appId}" 
                                });
                            }
                            Log($"Fetched {ownedGames.Count} owned games WITH NAMES via IPlayerService!");
                            return ownedGames;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"IPlayerService call failed: {ex.Message}");
                    }
                }        
                // 4. Ultimate Fallback: userdata (IDs only)
                var userDataUrl = "https://store.steampowered.com/dynamicstore/userdata/";
                try 
                {
                    Log("Attempting ultimate fallback to Dynamic Store API (userdata)...");
                    var (userDataContent, _) = await SteamAuthService.FetchProtectedPageAsync(userDataUrl, steamCookieFilePath); 
                    
                    using var doc = JsonDocument.Parse(userDataContent);
                    if (doc.RootElement.TryGetProperty("rgOwnedApps", out var rgOwnedApps))
                    {
                        foreach (var idElement in rgOwnedApps.EnumerateArray())
                        {
                            var appId = idElement.GetInt32();
                            string name = $"AppID {appId}";
                            
                            // Resolve name from master list if available
                            if (_masterAppListCache != null && _masterAppListCache.TryGetValue(appId, out var resolvedName))
                            {
                                name = resolvedName;
                            }
        
                            ownedGames.Add(new SteamWishlistItem 
                            {
                                AppId = appId, 
                                Name = name 
                            });
                        }
                        Log($"Fallback: Found {ownedGames.Count} owned games via Dynamic Store API.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Failed to fetch owned games via fallback: {ex.Message}");
                }
                
                return ownedGames;
            }}
