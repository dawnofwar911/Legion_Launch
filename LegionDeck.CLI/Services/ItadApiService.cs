using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web; // For HttpUtility.UrlEncode
using LegionDeck.CLI.Models; // For SteamWishlistItem, potentially other game models
using LegionDeck.CLI.Services; // For ConfigService

using System.Text.Json.Serialization; 

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
        [JsonPropertyName("id")] // Map 'id' (UUID) from API response to 'Plain' property
        public string? Plain { get; set; } // ITAD's internal game identifier (now mapped from id)
    }

    // Models for games/subs/v1
    public class ItadSubsResult : List<ItadGameSubscription>
    {
    }

    public class ItadGameSubscription
    {
        [JsonPropertyName("id")] // Explicitly map 'id' from JSON to this property
        public string? Id { get; set; } // The game's plain ID (UUID)
        [JsonPropertyName("subs")] // Explicitly map 'subs' from JSON to this property
        public List<ItadSubscription>? Subs { get; set; }
    }

    public class ItadSubscription
    {
        public int Id { get; set; } // ID of the subscription service
        [JsonPropertyName("name")] // Explicitly map 'name' from JSON to this property
        public string? Name { get; set; } // e.g., "Game Pass"
        [JsonPropertyName("leaving")] // Explicitly map 'leaving' from JSON to this property
        public string? Leaving { get; set; } // Date leaving subscription
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
        var url = $"{BaseUrl}games/search/v1?key={_apiKey}&title={encodedTitle}&limit=1"; // Reverted limit to 1
        
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<ItadGameLookupResult>(response);

            if (result == null || result.Count == 0)
            {
                Console.WriteLine($"[Error] Game '{title}' not found on ITAD.");
                return null;
            }
            // Simplified to take the first result, as it's the most relevant with limit=1
            return result.FirstOrDefault()?.Plain;
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

    public async Task<Dictionary<string, List<string>>> IsOnSubscriptionAsync(List<string> plains)
    {
        var subscriptionStatus = new Dictionary<string, List<string>>();
        
        var url = $"{BaseUrl}games/subs/v1?key={_apiKey}&region=GB"; 

        try
        {
            var requestBody = JsonSerializer.Serialize(plains);
            using var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            var responseMessage = await _httpClient.PostAsync(url, content);
            var response = await responseMessage.Content.ReadAsStringAsync();

            responseMessage.EnsureSuccessStatusCode();

            var result = JsonSerializer.Deserialize<List<ItadGameSubscription>>(response);

            if (result != null)
            {
                foreach (var plainId in plains)
                {
                    var gameSubscription = result.FirstOrDefault(gs => gs.Id == plainId);
                    var activeSubscriptions = new List<string>();

                    if (gameSubscription != null && gameSubscription.Subs != null)
                    {
                        foreach (var sub in gameSubscription.Subs)
                        {
                            if (sub.Leaving == null) // Check if the subscription is currently active
                            {
                                activeSubscriptions.Add(sub.Name ?? "Unknown Subscription");
                            }
                        }
                    }
                    
                    subscriptionStatus[plainId] = activeSubscriptions;
                }
            }
            return subscriptionStatus;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[Error] ITAD API POST request failed for plains '{string.Join(", ", plains)}' subscription: {ex.Message}");
            if (ex.StatusCode.HasValue)
            {
                Console.WriteLine($"[Debug] HTTP Status Code: {ex.StatusCode.Value}");
            }
            foreach (var plainId in plains)
            {
                subscriptionStatus[plainId] = new List<string> { "Error" }; // Indicate error
            }
            return subscriptionStatus;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Error] Failed to parse ITAD subscription info for plains '{string.Join(", ", plains)}': {ex.Message}");
            foreach (var plainId in plains)
            {
                subscriptionStatus[plainId] = new List<string> { "Error" }; // Indicate error
            }
            return subscriptionStatus;
        }
    }
}