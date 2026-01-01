using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using LegionDeck.Core.Models;
using System.Text.RegularExpressions;

namespace LegionDeck.Core.Services;

public class SubscriptionLibraryService
{
    private readonly HttpClient _httpClient;

    public SubscriptionLibraryService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task<List<SteamWishlistItem>> GetXboxGamePassGamesAsync()
    {
        var xboxLibrary = new XboxLibraryService();
        // Uses the user-verified 'All PC Games' SIGL ID for UK
        return await xboxLibrary.GetCatalogByIdAsync("fdd9e2a7-0fee-49f6-ad69-4354098401ff");
    }

    public async Task<List<SteamWishlistItem>> GetEaPlayGamesAsync()
    {
        // Default to scraping the web list for accuracy
        return await GetEaPlayStandardGamesAsync();
    }

    public async Task<List<SteamWishlistItem>> GetEaPlayStandardGamesAsync()
    {
        return await ScrapeEaGamescriptionsAsync("ea_pc");
    }

    public async Task<List<SteamWishlistItem>> GetEaPlayProGamesAsync()
    {
        return await ScrapeEaGamescriptionsAsync("ea_pc_pro");
    }

    public class EaScrapedGame
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        public bool IsPro { get; set; }
        public bool IsStandard { get; set; }
    }

    public async Task<List<EaScrapedGame>> GetAllEaGamesAsync()
    {
        var games = new List<EaScrapedGame>();
        var url = "https://gamescriptions.com/subscription/platform/ea";
        try
        {
            var html = await _httpClient.GetStringAsync(url);
            
            int pos = 0;
            while ((pos = html.IndexOf("game_name", pos)) != -1)
            {
                // Verify it's preceded by \" to ensure it's a key
                if (pos < 2 || html[pos - 1] != '"' || html[pos - 2] != '\\') { pos++; continue; }

                // Verify it is exactly "game_name" and not "game_name_date"
                if (html[pos + 9] != '\\' || html[pos + 10] != '"') { pos++; continue; }

                // find :
                int colon = html.IndexOf(':', pos + 9);
                if (colon == -1 || colon - pos > 20) { pos++; continue; }

                // The value starts after the colon and leading \"
                int startQuote = html.IndexOf('"', colon);
                if (startQuote == -1 || startQuote - colon > 10) { pos++; continue; }

                int titleStart = startQuote + 1;

                // Find the ending \"
                int titleEnd = html.IndexOf('"', titleStart);
                while (titleEnd > 0) 
                {
                     if (html[titleEnd - 1] == '\\') break;
                     titleEnd = html.IndexOf('"', titleEnd + 1);
                }

                if (titleEnd == -1) break;

                string rawName = html.Substring(titleStart, titleEnd - titleStart);
                string name = System.Text.RegularExpressions.Regex.Unescape(rawName);

                // Find game_id
                string id = name;
                int idLabelPos = html.LastIndexOf("game_id", pos);
                if (idLabelPos != -1 && pos - idLabelPos < 100)
                {
                    int idColon = html.IndexOf(':', idLabelPos);
                    if (idColon != -1)
                    {
                        int idEnd = html.IndexOf(',', idColon);
                        if (idEnd != -1)
                        {
                            id = html.Substring(idColon + 1, idEnd - idColon - 1).Trim();
                        }
                    }
                }

                // Find game_image
                string image = "";
                int imgLabelPos = html.IndexOf("game_image", titleEnd);
                if (imgLabelPos != -1 && imgLabelPos - titleEnd < 200)
                {
                    int imgColon = html.IndexOf(':', imgLabelPos);
                    int imgStartQuote = html.IndexOf('"', imgColon);
                    int imgEndQuote = html.IndexOf('"', imgStartQuote + 1);
                    // loop for escaped quote ender
                    while (imgEndQuote > 0) 
                    {
                         if (html[imgEndQuote - 1] == '\\') break;
                         imgEndQuote = html.IndexOf('"', imgEndQuote + 1);
                    }

                    if (imgStartQuote != -1 && imgEndQuote != -1)
                    {
                        string rawImg = html.Substring(imgStartQuote + 1, imgEndQuote - imgStartQuote - 1);
                        image = "https://images.gamescriptions.com/games/" + System.Text.RegularExpressions.Regex.Unescape(rawImg);
                    }
                }

                // Look for services in the following block (up to next game)
                int nextGame = html.IndexOf("game_id", titleEnd);
                int snippetEnd = nextGame != -1 ? nextGame : html.Length;
                int searchLimit = Math.Min(snippetEnd, titleEnd + 2000); 
                string snippet = html.Substring(titleEnd, searchLimit - titleEnd);

                bool isPro = snippet.Contains("ea_pc_pro");
                bool isStandard = snippet.Contains("\"ea_pc\"") || snippet.Contains("\\\"ea_pc\\\"");

                if (isPro || isStandard)
                {
                    if (!games.Any(g => g.Name == name))
                    {
                        System.Diagnostics.Debug.WriteLine($"[EA Scraper] Found game: {name} (ID: {id}, Pro: {isPro}, Std: {isStandard})");
                        // Also log to file for user
                        LogToStartup($"Scraped EA game: {name} (ID: {id})");
                        games.Add(new EaScrapedGame { Id = id, Name = name, Image = image, IsPro = isPro, IsStandard = isStandard });
                    }
                    else 
                    {
                        var existing = games.First(g => g.Name == name);
                        if (isPro) existing.IsPro = true;
                        if (isStandard) existing.IsStandard = true;
                        if (string.IsNullOrEmpty(existing.Image)) existing.Image = image;
                    }
                }

                pos = titleEnd + 1;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error scraping EA games: {ex.Message}");
        }
        return games;
    }

    private void LogToStartup(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SubscriptionLibraryService] {message}\n");
        }
        catch { }
    }

    private async Task<List<SteamWishlistItem>> ScrapeEaGamescriptionsAsync(string serviceKey)
    {
        var all = await GetAllEaGamesAsync();
        if (serviceKey == "ea_pc_pro")
            return all.Where(g => g.IsPro).Select(g => new SteamWishlistItem { Name = g.Name }).ToList();
        else
            return all.Where(g => g.IsStandard).Select(g => new SteamWishlistItem { Name = g.Name }).ToList();
    }

    public async Task<List<SteamWishlistItem>> GetUbisoftPlusGamesAsync()
    {
        var games = new List<SteamWishlistItem>();
        try
        {
            var localService = new UbisoftLocalService();
            var masterIdService = new UbisoftMasterIdService();
            var ownedIds = localService.GetOwnedUplayIds();
            LogToStartup($"Detected {ownedIds.Count} unique IDs in local Ubisoft cache.");

            var appId = "XELY3U4LOD";
            var apiKey = "5638539fd9edb8f2c6b024b49ec375bd";
            var indexName = "production__uk_ubisoft__products__en_GB__best_sellers";
            var url = $"https://{appId}-dsn.algolia.net/1/indexes/{indexName}/query";

            int page = 0;
            int totalPages = 1;

            while (page < totalPages)
            {
                var requestBody = new
                {
                    @params = $"filters=partOfUbisoftPlus=1&hitsPerPage=50&page={page}"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("X-Algolia-API-Key", apiKey);
                request.Headers.Add("X-Algolia-Application-Id", appId);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("nbPages", out var nbPagesProp))
                {
                    totalPages = nbPagesProp.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("hits", out var hits))
                {
                    foreach (var hit in hits.EnumerateArray())
                    {
                        // Check platform availability
                        bool isPc = false;
                        if (hit.TryGetProperty("platforms_availability", out var platProp))
                        {
                            var platString = platProp.GetString();
                            if (!string.IsNullOrEmpty(platString) && platString.Contains("PC (Digital)", StringComparison.OrdinalIgnoreCase))
                            {
                                isPc = true;
                            }
                        }
                        
                        // Fallback: check "Platform" field
                        if (!isPc && hit.TryGetProperty("Platform", out var pProp))
                        {
                            var p = pProp.GetString();
                            if (!string.IsNullOrEmpty(p) && p.Contains("PC", StringComparison.OrdinalIgnoreCase)) isPc = true;
                        }

                        if (!isPc) continue;

                                                string name = hit.GetProperty("title").GetString() ?? "";
                                                string storeId = hit.GetProperty("id").GetString() ?? ""; // Hex Store ID
                                                string launcherId = "";
                                                string image = "";
                                                string storeLink = "";
                        
                                                // Extract Uplay ID (Launcher ID)
                                                if (hit.TryGetProperty("productLauncherIDString", out var pidProp))
                                                {
                                                    launcherId = pidProp.GetString() ?? "";
                                                }
                                                
                                                // Extract Image
                                                if (hit.TryGetProperty("image_link", out var imgProp))
                                                {
                                                    image = imgProp.GetString() ?? "";
                                                }
                        
                                                // Extract Store Link
                                                if (hit.TryGetProperty("link", out var linkProp))
                                                {
                                                    storeLink = linkProp.GetString() ?? "";
                                                }
                        
                                                                                                if (!string.IsNullOrEmpty(name))
                        
                                                                                                {
                        
                                                                                                    // 1. Check the Master ID database (Highest priority for uninstalled games)
                        
                                                                                                    // This ensures For Honor uses 569 and Valhalla uses 13504
                        
                                                                                                    var verifiedLid = masterIdService.GetMasterId(name);
                        
                                                                                                    bool isMasterMatch = verifiedLid.HasValue;
                        
                                                                        
                        
                                                                                                                                // 2. If not in Master List, try to find it in local account cache (by name match)
                        
                                                                        
                        
                                                                                                                                if (!verifiedLid.HasValue)
                        
                                                                        
                        
                                                                                                                                {
                        
                                                                        
                        
                                                                                                                                    verifiedLid = localService.GetUplayIdByGameName(name);
                        
                                                                        
                        
                                                                                                                                }
                        
                                                                        
                        
                                                                                                    
                        
                                                                        
                        
                                                                                                                                bool isInLocalCache = false;
                        
                                                                        
                        
                                                                                                                                
                        
                                                                        
                        
                                                                                                                                // Check Master ID
                        
                                                                        
                        
                                                                                                                                if (verifiedLid.HasValue && ownedIds.Contains(verifiedLid.Value)) isInLocalCache = true;
                        
                                                                        
                        
                                                                                                                                
                        
                                                                        
                        
                                                                                                                                // Check Launcher ID from Store (fallback)
                        
                                                                        
                        
                                                                                                                                if (!isInLocalCache && !string.IsNullOrEmpty(launcherId) && uint.TryParse(launcherId, out var lid) && ownedIds.Contains(lid)) isInLocalCache = true;
                        
                                                                        
                        
                                                                                                    
                        
                                                                        
                        
                                                                                                                                string finalLid = verifiedLid?.ToString() ?? launcherId;
                        
                                                                        
                        
                                                                                                    var existing = games.FirstOrDefault(g => g.Name == name);
                        
                                                                                                    if (existing == null)
                        
                                                                                                    {
                        
                                                                                                        var item = new SteamWishlistItem 
                        
                                                                                                        { 
                        
                                                                                                            Name = name, 
                        
                                                                                                            SteamAppId = storeId, 
                        
                                                                                                            ImgCapsule = image,
                        
                                                                                                            Type = isInLocalCache ? "InCache" : (verifiedLid.HasValue ? "Master" : "Store"),
                        
                                                                                                            ReleaseDate = storeLink
                        
                                                                                                        };
                        
                                                                                                        if (!string.IsNullOrEmpty(finalLid)) item.PlainIds.Add(finalLid);
                        
                                                                                                        games.Add(item);
                        
                                                                                                    }                                                    else if (string.IsNullOrEmpty(existing.SteamAppId) && !string.IsNullOrEmpty(storeId))
                                                    {
                                                        existing.SteamAppId = storeId;
                                                        existing.ReleaseDate = storeLink;
                                                        if (isInLocalCache) existing.Type = "InCache";
                                                        if (!string.IsNullOrEmpty(finalLid) && !existing.PlainIds.Contains(finalLid)) 
                                                            existing.PlainIds.Add(finalLid);
                                                    }
                                                }                    }
                }
                page++;
            }
            LogToStartup($"Ubisoft Sync: Fetched {games.Count} titles via Algolia.");
        } 
        catch (Exception ex) 
        { 
            LogToStartup($"Ubisoft Sync Error: {ex.Message}");
        }
        return games;
    }
}
