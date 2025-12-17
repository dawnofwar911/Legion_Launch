using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using LegionDeck.CLI.Models;
using System.Net;
using System;
using System.Linq; 
using System.IO;

namespace LegionDeck.CLI.Services;

public class SteamWishlistService
{

    public SteamWishlistService()
    {
        // Constructor is now empty as there's no HttpClient to initialize for authenticated calls.
    }


    public async Task<List<SteamWishlistItem>> GetWishlistAsync(string? specificSteamIdOrName = null)
    {
        var wishlist = new List<SteamWishlistItem>();
        string requestUrl = string.Empty;
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var steamCookieFilePath = Path.Combine(authTokensPath, "steam_cookies.json");
        
        var userDataUrl = "https://store.steampowered.com/dynamicstore/userdata/";
        Console.WriteLine($"Attempting to fetch wishlist IDs from: {userDataUrl}");
        
        var (userDataContent, userDataFinalUrl) = await SteamAuthService.FetchProtectedPageAsync(userDataUrl, steamCookieFilePath); 
        string processedResponse = userDataContent;

        // --- Try fetching from Dynamic Store UserData first (Reliable for AppIDs, no Profile ID needed) ---
        try 
        {
            if (userDataContent.Contains("login_btn_signin") || userDataContent.Contains("global_login_btn"))
            {
                Console.WriteLine("[Debug] Dynamic Store returned login page HTML. Falling back to scraping.");
            }
            else if (processedResponse.Contains("<pre"))
            {
                var preStart = processedResponse.IndexOf(">") + 1;
                var preEnd = processedResponse.LastIndexOf("<");
                if (preStart > 0 && preEnd > preStart)
                {
                    processedResponse = processedResponse.Substring(preStart, preEnd - preStart);
                }
            } else if (!processedResponse.Trim().StartsWith("{") && !processedResponse.Trim().StartsWith("["))
            {
                Console.WriteLine("[Debug] Dynamic Store returned unexpected non-JSON/non-HTML content.");
            }
            else // It looks like JSON, try to parse
            {
 
                
                using var doc = JsonDocument.Parse(processedResponse);
                if (doc.RootElement.TryGetProperty("rgWishlist", out var rgWishlist))
                {
                    Console.WriteLine("Successfully retrieved wishlist IDs from Dynamic Store.");
                    foreach (var id in rgWishlist.EnumerateArray())
                    {
                        var appId = id.GetInt32();
                        wishlist.Add(new SteamWishlistItem 
                        { 
                            AppId = appId, 
                            Name = $"AppID {appId}" 
                        });
                    }
                }
            }

            if (wishlist.Count > 0)
            {
                Console.WriteLine($"Found {wishlist.Count} items via Dynamic Store API.");
                Console.WriteLine("DEBUG: Dynamic Store fetch successful, returning wishlist.");
                return wishlist; 
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Dynamic Store fetch failed (will try fallback): {ex.Message}");
            Console.WriteLine($"[Debug] Dynamic Store raw response (on error): {userDataContent.Substring(0, Math.Min(userDataContent.Length, 500))}");
        }
        
        return wishlist;
    }

    public async Task<string?> GetAppDetailsAsync(int appId)
    {
        // Use a local HttpClient for non-authenticated public API calls
        using var httpClient = new HttpClient();
        var url = $"https://store.steampowered.com/api/appdetails/?appids={appId}&cc=us&l=en";
        try
        {
            var response = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty(appId.ToString(), out var appElement) &&
                appElement.TryGetProperty("success", out var successElement) &&
                successElement.GetBoolean() == true &&
                appElement.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("name", out var nameElement))
            {
                return nameElement.GetString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to get app details for AppID {appId}: {ex.Message}");
        }
        return null;
    }

    private class SimpleWishlistItem 
    {
        public int appid { get; set; }
        public int priority { get; set; }
        public int added { get; set; }
    }
}
