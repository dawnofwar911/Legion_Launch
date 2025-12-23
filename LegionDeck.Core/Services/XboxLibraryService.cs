using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using LegionDeck.Core.Models;

namespace LegionDeck.Core.Services;

public class XboxLibraryService
{
    private readonly HttpClient _httpClient;

    public XboxLibraryService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LegionDeck/1.0");
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [XboxLibraryService] {message}\n");
        }
        catch { }
    }

    public async Task<List<SteamWishlistItem>> GetPersonalLibraryAsync()
    {
        var games = new List<SteamWishlistItem>();
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var xboxCookieFilePath = Path.Combine(authTokensPath, "xbox_cookies.json");

        if (!File.Exists(xboxCookieFilePath)) 
        {
            Log("Xbox cookie file missing. User not authenticated.");
            return games;
        }

        try
        {
            Log("Fetching Xbox Personal Library via TitleHub...");
            
            var authService = new XboxAuthService();
            var auth = await authService.GetXstsTokenAsync();
            
            if (string.IsNullOrEmpty(auth.AuthHeader) || string.IsNullOrEmpty(auth.Xuid))
            {
                Log("Failed to get XSTS token or Xuid. User might need to log in again.");
                return games;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            client.DefaultRequestHeaders.Add("x-xbl-contract-version", "2");
            client.DefaultRequestHeaders.Add("Authorization", auth.AuthHeader);
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US");

            // Use 'detail' decoration which provides game names
            var url = $"https://titlehub.xboxlive.com/users/xuid({auth.Xuid})/titles/titlehistory/decoration/detail";
            var response = await client.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                Log($"Xbox Personal Library Error ({response.StatusCode}): {err}");
                return games;
            }

            var json = await response.Content.ReadAsStringAsync();
            Log($"TitleHub response received (Length: {json.Length}). Preview: {(json.Length > 200 ? json.Substring(0, 200) : json)}");

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("titles", out var titles))
            {
                foreach (var title in titles.EnumerateArray())
                {
                    var name = title.TryGetProperty("name", out var n) ? n.GetString() : "Unknown Title";
                    var type = title.TryGetProperty("type", out var t) ? t.GetString() : "Unknown";
                    
                    Log($"Found Title: {name} (Type: {type})");

                    if (type != null && type.Equals("Game", StringComparison.OrdinalIgnoreCase))
                    {
                        games.Add(new SteamWishlistItem
                        {
                            AppId = 0, 
                            Name = name ?? "Unknown Game"
                        });
                    }
                }
                Log($"Xbox Sync Complete. Found {games.Count} personal games.");
            }
            else
            {
                Log("TitleHub response did not contain 'titles' property. Response might be a login redirect or error.");
                if (json.Contains("login", StringComparison.OrdinalIgnoreCase)) Log("Detection: Response looks like a Login Redirect.");
            }
        }
        catch (Exception ex)
        {
            Log($"Xbox Personal Library Exception: {ex.Message}");
        }

        return games;
    }

    public async Task<List<SteamWishlistItem>> GetGamePassGamesAsync()
    {
        var games = new List<SteamWishlistItem>();
        
        // PC Game Pass List ID
        var listId = "29a057a0-ed2d-46a7-aa4f-453629c74825";
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

            // Fetch details in batches of 20 (Microsoft API limit)
            for (int i = 0; i < ids.Count; i += 20)
            {
                var batchIds = ids.Skip(i).Take(20).ToList();
                var batchGames = await GetProductDetailsBatchAsync(batchIds);
                games.AddRange(batchGames);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching Game Pass catalog: {ex.Message}");
        }

        return games;
    }

    private async Task<List<SteamWishlistItem>> GetProductDetailsBatchAsync(List<string> ids)
    {
        var games = new List<SteamWishlistItem>();
        var idsParam = string.Join(",", ids);
        var detailsUrl = $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={idsParam}&market=US&languages=en-us&MS-CV=DGU1mcuE00WOfm3m.1";

        try
        {
            var response = await _httpClient.GetStringAsync(detailsUrl);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("Products", out var products))
            {
                foreach (var product in products.EnumerateArray())
                {
                    var productId = product.GetProperty("ProductId").GetString()!;
                    var localizedProperties = product.GetProperty("LocalizedProperties").EnumerateArray().First();
                    var title = localizedProperties.GetProperty("ProductTitle").GetString()!;

                    games.Add(new SteamWishlistItem
                    {
                        AppId = 0, // Xbox uses string IDs, we'll store it in a custom field or reuse
                        Name = title,
                        // We can store ProductID in a custom way if needed, but for display Name is enough
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching product details: {ex.Message}");
        }

        return games;
    }
}
