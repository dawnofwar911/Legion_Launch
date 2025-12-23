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
        var games = new List<SteamWishlistItem>();
        var listId = "29a057a0-ed2d-46a7-aa4f-453629c74825"; // PC Game Pass
        var catalogUrl = $"https://catalog.gamepass.com/sigls/v2?id={listId}&language=en-us&market=US";

        try
        {
            var response = await _httpClient.GetStringAsync(catalogUrl);
            using var doc = JsonDocument.Parse(response);
            
            var ids = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                {
                    ids.Add(idProp.GetString()!);
                }
            }

            for (int i = 0; i < ids.Count; i += 20)
            {
                var batchIds = ids.Skip(i).Take(20).ToList();
                var idsParam = string.Join(",", batchIds);
                var detailsUrl = $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={idsParam}&market=US&languages=en-us&MS-CV=DGU1mcuE00WOfm3m.1";

                var detailsResponse = await _httpClient.GetStringAsync(detailsUrl);
                using var detailsDoc = JsonDocument.Parse(detailsResponse);

                if (detailsDoc.RootElement.TryGetProperty("Products", out var products))
                {
                    foreach (var product in products.EnumerateArray())
                    {
                        var localizedProperties = product.GetProperty("LocalizedProperties").EnumerateArray().First();
                        var title = localizedProperties.GetProperty("ProductTitle").GetString()!;

                        games.Add(new SteamWishlistItem
                        {
                            AppId = 0,
                            Name = title
                        });
                    }
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Xbox Error: {ex.Message}"); } 
        return games;
    }

    public async Task<List<SteamWishlistItem>> GetEaPlayGamesAsync()
    {
        var games = new List<SteamWishlistItem>();
        // EA Play API (Publicly accessible search for subGroup)
        var url = "https://api1.origin.com/xcloud/v1/search/pc?fq=subscriptionGroup:ea-play&facet=subscriptionGroup&sort=rank%20desc&start=0&rows=500";

        try
        {
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("games", out var gamesRoot) && 
                gamesRoot.TryGetProperty("game", out var gameList))
            {
                foreach (var game in gameList.EnumerateArray())
                {
                    var title = game.GetProperty("gameName").GetString()!;
                    games.Add(new SteamWishlistItem
                    {
                        AppId = 0,
                        Name = title
                    });
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"EA Error: {ex.Message}"); } 
        return games;
    }

    public async Task<List<SteamWishlistItem>> GetUbisoftPlusGamesAsync()
    {
        var games = new List<SteamWishlistItem>();
        // Scraping the Ubisoft+ games page
        var url = "https://store.ubisoft.com/uk/ubisoftplus/games";

        try
        {
            // Note: This is a public page but might be rendered client-side.
            // For now, we'll try a simple scrape. If it fails, we might need WebView2.
            var response = await _httpClient.GetStringAsync(url);
            
            // Extract game titles from HTML (Simple Regex approach)
            // Example: data-game-title="GAME_NAME"
            var matches = Regex.Matches(response, "data-game-title=\"(.*?)\"");
            foreach (Match match in matches)
            {
                var title = match.Groups[1].Value;
                if (!games.Any(g => g.Name == title))
                {
                    games.Add(new SteamWishlistItem { AppId = 0, Name = title });
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Ubisoft Error: {ex.Message}"); } 
        return games;
    }
}
