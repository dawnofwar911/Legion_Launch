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

    // Fallback map for games with incorrect/internal names in the library API
    private static readonly Dictionary<string, string> _knownGameIdMap = new()
    {
        { "d4dd03bc745c47aaa454189a2b4525ec", "Railgrade" }, // bucatini Production
        { "22530dcaf47c4170886e83bf0b94229d", "Voidtrain" }, // Volta
        { "a0c3344c008d4475a9a29a7a0b6189b8", "Voidtrain" }  // AppName for Volta record
    };

    private Dictionary<string, string?> _egdataNameCache = new();
    private readonly string _cachePath;

    public EpicLibraryService(ConfigService configService)
    {
        _configService = configService;
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "MetadataCache");
        Directory.CreateDirectory(cacheDir);
        _cachePath = Path.Combine(cacheDir, "epic_titles.json");
        LoadTitleCache();
    }

    private void LoadTitleCache()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var json = File.ReadAllText(_cachePath);
                _egdataNameCache = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
            }
        }
        catch { }
    }

    private void SaveTitleCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_egdataNameCache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cachePath, json);
        }
        catch { }
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
                SaveTitleCache();
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

            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("records", out var records))
            {
                var pageGames = new List<LocalLibraryService.InstalledGame>();
                foreach (var record in records.EnumerateArray())
                {
                    // Filter for applications only
                    var type = record.TryGetProperty("recordType", out var typeProp) ? typeProp.GetString() : null;
                    if (type != "APPLICATION") continue;

                    // Prioritize title, then displayName, then sandboxName
                    var title = record.TryGetProperty("title", out var t0) ? t0.GetString() : 
                                record.TryGetProperty("displayName", out var t1) ? t1.GetString() : 
                                record.TryGetProperty("sandboxName", out var t2) ? t2.GetString() : null;

                    // Prioritize appName for ID, fallback to catalogItemId
                    var catalogItemId = record.TryGetProperty("catalogItemId", out var cId) ? cId.GetString() : null;
                    var appName = record.TryGetProperty("appName", out var aName) ? aName.GetString() : null;
                    var productId = record.TryGetProperty("productId", out var pId) ? pId.GetString() : null;
                    
                    var appId = appName ?? 
                                (record.TryGetProperty("appId", out var a2) ? a2.GetString() : null) ?? 
                                catalogItemId;

                    // 1. Check for hardcoded overrides first
                    if (!string.IsNullOrEmpty(catalogItemId) && _knownGameIdMap.TryGetValue(catalogItemId, out var mappedName))
                    {
                        title = mappedName;
                    }
                    else if (!string.IsNullOrEmpty(appId) && _knownGameIdMap.TryGetValue(appId, out mappedName))
                    {
                        title = mappedName;
                    }

                    if (appId == "UnrealTournamentDev") continue; // Ignore UT Marketplace

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

                // Deduplicate by Name
                var deduplicated = pageGames
                    .GroupBy(g => g.Name)
                    .Select(group => group.OrderBy(g => g.Id.Length).First())
                    .ToList();

                games.AddRange(deduplicated);
                Log($"Parsed {deduplicated.Count} unique games from this page.");
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

    private async Task<string?> FetchNameFromEgdataAsync(string id)
    {
        if (_egdataNameCache.TryGetValue(id, out var cached)) return cached;

        using var client = new HttpClient();
        
        // Try /offers/ endpoint first
        string? name = await TryScrapeUrl(client, $"https://egdata.app/offers/{id}");
        
        // If not found, try /items/ endpoint
        if (string.IsNullOrEmpty(name))
        {
            name = await TryScrapeUrl(client, $"https://egdata.app/items/{id}");
        }

        if (!string.IsNullOrEmpty(name))
        {
            _egdataNameCache[id] = name;
            return name;
        }
        
        // Do not cache nulls, so we retry next time
        return null;
    }

    private async Task<string?> TryScrapeUrl(HttpClient client, string url)
    {
        try
        {
            var html = await client.GetStringAsync(url);
            
            // <title>Game Name | egdata.app</title>
            // For items page it might be "Game Name | Item | egdata.app" or similar
            var match = System.Text.RegularExpressions.Regex.Match(html, @"<title>(.*?) \|.*egdata\.app</title>");
            if (match.Success)
            {
                var name = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
                Log($"Scraped name '{name}' from {url}");
                return name;
            }
            else
            {
                 Log($"Regex failed for {url}");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to scrape {url}: {ex.Message}");
        }
        return null;
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
