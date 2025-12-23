using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using LegionDeck.Core.Models;
using System.Text.RegularExpressions;

namespace LegionDeck.Core.Services;

public class XboxLibraryService
{
    private readonly HttpClient _httpClient;

    public XboxLibraryService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [XboxLibraryService] {message}\n");
        }
        catch {{ }}
    }

    public async Task<List<SteamWishlistItem>> GetPersonalLibraryAsync()
    {
        var games = new List<SteamWishlistItem>();
        try
        {
            Log("Fetching Xbox Personal Library via TitleHistory...");
            var authService = new XboxAuthService();
            var auth = await authService.GetXstsTokenAsync();
            if (string.IsNullOrEmpty(auth.AuthHeader) || string.IsNullOrEmpty(auth.Xuid)) return games;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            client.DefaultRequestHeaders.Add("x-xbl-contract-version", "2");
            client.DefaultRequestHeaders.Add("Authorization", auth.AuthHeader);
            client.DefaultRequestHeaders.Add("Accept-Language", "en-GB");

            var url = $"https://titlehub.xboxlive.com/users/xuid({auth.Xuid})/titles/titlehistory/decoration/detail";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return games;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("titles", out var titles))
            {
                foreach (var title in titles.EnumerateArray())
                {
                    var name = title.GetProperty("name").GetString();
                    var type = title.GetProperty("type").GetString();
                    if (type != null && type.Contains("Game", StringComparison.OrdinalIgnoreCase))
                    {
                        games.Add(new SteamWishlistItem { Name = name });
                    }
                }
            }
            Log($"Xbox Personal Sync: Found {games.Count} games.");
        }
        catch (Exception ex) { Log($"Xbox Personal Library Error: {ex.Message}"); }
        return games;
    }

    public async Task<List<SteamWishlistItem>> GetCatalogByIdAsync(string siglId)
    {
        var games = new List<SteamWishlistItem>();
        var catalogUrl = $"https://catalog.gamepass.com/sigls/v2?id={siglId}&language=en-gb&market=GB";

        try
        {
            Log($"Fetching Game Pass catalog for SIGL ID {siglId}...");
            var response = await _httpClient.GetStringAsync(catalogUrl);
            using var doc = JsonDocument.Parse(response);
            var ids = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id) && id.Length >= 10) ids.Add(id);
                }
            }

            Log($"SIGL {siglId} found {ids.Count} Product IDs. Fetching details...");

            for (int i = 0; i < ids.Count; i += 20)
            {
                var batchIds = ids.Skip(i).Take(20).ToList();
                var batchGames = await GetProductDetailsBatchAsync(batchIds);
                games.AddRange(batchGames);
                if (i > 0 && i % 100 == 0) await Task.Delay(200);
            }
            Log($"Xbox Catalog Sync ({siglId}): Successfully fetched {games.Count} titles.");
        }
        catch (Exception ex) { Log($"Game Pass Catalog Error ({siglId}): {ex.Message}"); }
        return games;
    }

    public async Task<List<SteamWishlistItem>> GetGamePassGamesAsync()
    {
        var games = new List<SteamWishlistItem>();
        
        // All PC Games List
        var pcListId = "fdd9e2a7-0fee-49f6-ad69-4354098401ff";

        try
        {
            Log("Fetching Xbox PC Game Pass catalog...");
            
            games = await GetCatalogByIdAsync(pcListId);

            Log($"Xbox Catalog Sync: Successfully fetched {games.Count} total titles.");
        }
        catch (Exception ex) { Log($"Game Pass Catalog Error: {ex.Message}"); }
        return games;
    }

    private async Task<List<SteamWishlistItem>> GetProductDetailsBatchAsync(List<string> ids)
    {
        var games = new List<SteamWishlistItem>();
        var idsParam = string.Join(",", ids);
        // Correct display catalog endpoint for GB
        var detailsUrl = $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={idsParam}&market=GB&languages=en-gb&MS-CV=DGU1mcuE00WOfm3m.1";

        try
        {
            var response = await _httpClient.GetStringAsync(detailsUrl);
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("Products", out var products))
            {
                foreach (var product in products.EnumerateArray())
                {
                    var productId = product.GetProperty("ProductId").GetString();
                    var localizedProperties = product.GetProperty("LocalizedProperties").EnumerateArray().First();
                    var title = localizedProperties.GetProperty("ProductTitle").GetString();

                    bool isPc = false;
                    if (product.TryGetProperty("DisplaySkuAvailabilities", out var skus))
                    {
                        var raw = skus.GetRawText();
                        // Verify this is a PC game
                        if (raw.Contains("Windows.Desktop", StringComparison.OrdinalIgnoreCase) || 
                            raw.Contains("PC", StringComparison.OrdinalIgnoreCase)) isPc = true;
                    }

                    if (isPc && !string.IsNullOrEmpty(title))
                    {
                        games.Add(new SteamWishlistItem { Name = title, SteamAppId = productId });
                    }
                }
            }
        } 
        catch {{ }}
        return games;
    }
}