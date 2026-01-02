using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using LegionDeck.Core.Models;

namespace LegionDeck.Core.Services;

public class EpicLibraryService
{
    private readonly ConfigService _configService;
    private const string LibraryUrl = "https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true&platform=Windows";

    public EpicLibraryService(ConfigService configService)
    {
        _configService = configService;
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [EpicLibraryService] {message}\n");
        }
        catch {{ }}
    }

    public async Task<List<LocalLibraryService.InstalledGame>> GetOwnedGamesAsync()
    {
        var games = new List<LocalLibraryService.InstalledGame>();
        var tokens = await LoadTokensAsync();

        if (tokens == null || string.IsNullOrEmpty(tokens.access_token))
        {
            Log("No valid Epic tokens found. Skipping sync.");
            return games;
        }

        try
        {
            var result = await FetchLibraryRecursiveAsync(tokens.access_token, tokens.token_type ?? "bearer");
            if (result == null) // Potential auth error
            {
                Log("Initial fetch failed. Attempting token refresh...");
                var authService = new EpicAuthService();
                if (await authService.RefreshSessionAsync())
                {
                    tokens = await LoadTokensAsync();
                    if (tokens != null && !string.IsNullOrEmpty(tokens.access_token))
                    {
                        result = await FetchLibraryRecursiveAsync(tokens.access_token, tokens.token_type ?? "bearer");
                    }
                }
            }

            if (result != null)
            {
                games = result;
                Log($"Epic library sync complete. Found {games.Count} games.");
            }
        }
        catch (Exception ex)
        {
            Log($"Error during Epic library sync: {ex.Message}");
        }

        return games;
    }

    private async Task<List<LocalLibraryService.InstalledGame>?> FetchLibraryRecursiveAsync(string accessToken, string tokenType)
    {
        var games = new List<LocalLibraryService.InstalledGame>();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(tokenType, accessToken);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) EpicGamesLauncher");

        string? nextCursor = null;
        do
        {
            var url = string.IsNullOrEmpty(nextCursor) ? LibraryUrl : $"{LibraryUrl}&cursor={nextCursor}";
            Log($"Requesting Epic Library: {url}");
            
            var response = await client.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            
            if (response.StatusCode == HttpStatusCode.Unauthorized) 
            {
                Log($"Unauthorized! Response: {json}");
                return null; 
            }
            
            if (!response.IsSuccessStatusCode)
            {
                Log($"Failed to fetch Epic library: {response.StatusCode} - {json}");
                break;
            }

            Log($"Library Response (first 1000 chars): {(json.Length > 1000 ? json.Substring(0, 1000) : json)}");

            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("records", out var records))
            {
                var pageGames = new List<LocalLibraryService.InstalledGame>();
                foreach (var record in records.EnumerateArray())
                {
                    // Filter for applications only
                    var type = record.TryGetProperty("recordType", out var typeProp) ? typeProp.GetString() : null;
                    if (type != "APPLICATION") continue;

                    // Prioritize sandboxName for title, as observed in logs (e.g. "Sid Meier's Civilization VI")
                    var title = record.TryGetProperty("sandboxName", out var t0) ? t0.GetString() : 
                                record.TryGetProperty("title", out var t1) ? t1.GetString() : 
                                record.TryGetProperty("displayName", out var t2) ? t2.GetString() : null;

                    // Prioritize appName for ID, fallback to catalogItemId
                    var appId = record.TryGetProperty("appName", out var a1) ? a1.GetString() : 
                                record.TryGetProperty("appId", out var a2) ? a2.GetString() : 
                                record.TryGetProperty("catalogItemId", out var a3) ? a3.GetString() : null;

                    if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(appId))
                    {
                        pageGames.Add(new LocalLibraryService.InstalledGame
                        {
                            Id = appId,
                            Name = title,
                            Source = "Epic",
                            IsInstalled = false
                        });
                    }
                }

                // Deduplicate by Name (sandboxName). 
                // DLCs often share the same sandboxName but have longer appNames (e.g. "Kinglet" vs "KingletAztec")
                var deduplicated = pageGames
                    .GroupBy(g => g.Name)
                    .Select(group => group.OrderBy(g => g.Id.Length).First())
                    .ToList();

                games.AddRange(deduplicated);
                Log($"Parsed {deduplicated.Count} unique games from this page (out of {pageGames.Count} total records).");
            }
            else
            {
                Log("No 'records' property found in Epic response.");
            }

            nextCursor = doc.RootElement.TryGetProperty("responseMetadata", out var meta) && 
                         meta.TryGetProperty("nextCursor", out var cursorProp) ? cursorProp.GetString() : null;

        } while (!string.IsNullOrEmpty(nextCursor));

        return games;
    }

    private async Task<EpicTokens?> LoadTokensAsync()
    {
        try
        {
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var tokenPath = Path.Combine(authTokensPath, "epic_tokens.json");

            if (File.Exists(tokenPath))
            {
                var json = await File.ReadAllTextAsync(tokenPath);
                return JsonSerializer.Deserialize<EpicTokens>(json);
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to load Epic tokens: {ex.Message}");
        }
        return null;
    }

    private class EpicTokens
    {
        public string? access_token { get; set; }
        public string? refresh_token { get; set; }
        public string? token_type { get; set; }
        public int expires_in { get; set; }
    }
}
