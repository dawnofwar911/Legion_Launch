using System;
using System.Collections.Generic;
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

                // find :
                int colon = html.IndexOf(':', pos + 9);
                if (colon == -1 || colon - pos > 15) { pos++; continue; }

                // The value starts after the colon and leading \"
                int startQuote = html.IndexOf('"', colon);
                if (startQuote == -1 || startQuote - colon > 10) { pos++; continue; }

                int titleStart = startQuote + 1;

                // Find the ending \"
                int titleEnd = -1;
                int search = titleStart;
                while ((search = html.IndexOf('"', search)) != -1)
                {
                    if (search > 0 && html[search - 1] == '\\')
                    {
                        titleEnd = search - 1;
                        break;
                    }
                    search++;
                }

                if (titleEnd == -1) break;

                string name = html.Substring(titleStart, titleEnd - titleStart);

                // Find game_id in the vicinity (it usually precedes game_name)
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

                // Look for services in the following block (up to next game)
                int nextGame = html.IndexOf("game_id", titleEnd);
                int snippetEnd = nextGame != -1 ? nextGame : html.Length;
                snippetEnd = Math.Min(snippetEnd, titleEnd + 5000);
                string snippet = html.Substring(titleEnd, snippetEnd - titleEnd);

                bool isPro = snippet.Contains("ea_pc_pro");
                bool isStandard = snippet.Contains("ea_pc\\\"") || snippet.Contains("ea_pc,") || (snippet.Contains("ea_pc") && !snippet.Contains("ea_pc_pro"));

                if (isPro || isStandard)
                {
                    string cleanName = System.Text.RegularExpressions.Regex.Unescape(name);
                    if (!games.Any(g => g.Name == cleanName))
                    {
                        games.Add(new EaScrapedGame { Id = id, Name = cleanName, IsPro = isPro, IsStandard = isStandard });
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
        var url = "https://store.ubisoft.com/uk/ubisoftplus/games";
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var matches = Regex.Matches(response, "data-game-title=\"(.*?)\"");
            foreach (Match match in matches)
            {
                var title = match.Groups[1].Value;
                if (!games.Any(g => g.Name == title)) games.Add(new SteamWishlistItem { Name = title });
            }
        } catch { }
        return games;
    }
}
