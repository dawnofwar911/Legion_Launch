using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web; // For HttpUtility.UrlEncode
using LegionDeck.CLI.Models; // For SteamWishlistItem, potentially other game models
using LegionDeck.CLI.Services; // For ConfigService

namespace LegionDeck.CLI.Services;

public class ItadApiService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigService _configService;
    private readonly string _apiKey;
    private const string BaseUrl = "https://api.isthereanydeal.com/";

    public ItadApiService(ConfigService configService)
    {
        _configService = configService;
        _apiKey = _configService.GetApiKey("ITAD") ?? throw new InvalidOperationException("ITAD API key is not configured.");
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LegionDeck CLI/1.0");
        // Removed X-Api-Key header as per Node.js client observation
    }

    // ITAD API models will go here
    // First, a model to hold ITAD's internal game ID (plain) - v1 API returns a direct list
    public class ItadGameLookupResult : List<ItadGameLookupItem>
    {
    }

    public class ItadGameLookupItem
    {
        public string? Title { get; set; }
        public string? Plain { get; set; } // ITAD's internal game identifier
    }

    // Models for games/overview/v1
    public class ItadGameOverviewResult
    {
        public ItadGameOverviewData? Data { get; set; }
    }

    public class ItadGameOverviewData
    {
        public string? Title { get; set; }
        public string? Plain { get; set; }
        public Dictionary<string, List<ItadOffer>>? Offers { get; set; } // Offers grouped by shop name
    }

    public class ItadOffer
    {
        public string? Shop { get; set; }
        public decimal? Price { get; set; }
        public decimal? PriceCut { get; set; }
        public string? Url { get; set; }
        public List<string>? Drm { get; set; } // Digital Rights Management, includes "xboxgamepass" if available
    }

    private string CleanGameTitle(string title)
    {
        // Remove common special characters and trademark symbols
        title = title.Replace("®", "").Replace("™", "").Replace("©", "");
        // Remove anything that's not alphanumeric or space
        title = System.Text.RegularExpressions.Regex.Replace(title, @"[^a-zA-Z0-9\s]", "");
        // Replace multiple spaces with a single space
        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
        return title;
    }

    public async Task<string?> GetPlainIdAsync(string title)
    {
        var cleanedTitle = CleanGameTitle(title);
        var encodedTitle = HttpUtility.UrlEncode(cleanedTitle);
        var url = $"{BaseUrl}games/search/v1?key={_apiKey}&title={encodedTitle}&limit=1"; // Changed 'q' to 'title'
        
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<ItadGameLookupResult>(response);
            if (result == null || result.Count == 0)
            {
                Console.WriteLine($"[Error] Game '{title}' not found on ITAD.");
                return null;
            }
            return result?.FirstOrDefault()?.Plain;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[Error] ITAD API request failed for '{title}': {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Error] Failed to parse ITAD game lookup for '{title}': {ex.Message}");
            return null;
        }
    }

    public async Task<bool> IsOnSubscriptionAsync(string plain, string subscriptionService)
    {
        Console.WriteLine($"[Debug] Checking subscription for plain '{plain}' on service '{subscriptionService}'.");
        var url = $"{BaseUrl}games/overview/v1?key={_apiKey}&plain={plain}";

        try
        {
            var responseMessage = await _httpClient.GetAsync(url);
            var response = await responseMessage.Content.ReadAsStringAsync(); // Read content regardless of success

            Console.WriteLine($"[Debug] ITAD Overview Raw Response for plain '{plain}': {response.Substring(0, Math.Min(response.Length, 500))}"); // Print first 500 chars for debug

            responseMessage.EnsureSuccessStatusCode(); // Throws HttpRequestException for non-success codes

            var result = JsonSerializer.Deserialize<ItadGameOverviewResult>(response);

            if (result?.Data?.Offers != null)
            {
                foreach (var shopOffers in result.Data.Offers.Values)
                {
                    foreach (var offer in shopOffers)
                    {
                        if (offer.Drm != null && offer.Drm.Contains(subscriptionService, StringComparer.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"[Debug] Found '{plain}' on '{subscriptionService}' via DRM: {string.Join(", ", offer.Drm)}");
                            return true;
                        }
                    }
                }
            }
            Console.WriteLine($"[Debug] '{plain}' not found on '{subscriptionService}' in ITAD offers.");
            return false;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[Error] ITAD API request failed for '{plain}' subscription: {ex.Message}");
            // Now, also print the response body if available, as that usually contains API-specific error details.
            if (ex.StatusCode.HasValue)
            {
                Console.WriteLine($"[Debug] HTTP Status Code: {ex.StatusCode.Value}");
            }
            return false;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Error] Failed to parse ITAD subscription info for '{plain}': {ex.Message}");
            return false;
        }
    }
}