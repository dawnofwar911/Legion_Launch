using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using LegionDeck.CLI.Services; 
using System.Text.Json.Serialization; 
using System.Linq;

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
    }

    public class ItadGameLookupResult : List<ItadGameLookupItem>
    {
    }

    public class ItadGameLookupItem
    {
        [JsonPropertyName("title")] 
        public string? Title { get; set; }
        [JsonPropertyName("id")] 
        public string? Plain { get; set; } 
    }

    public class ItadGameSubscription
    {
        [JsonPropertyName("id")] 
        public string? Id { get; set; } 
        [JsonPropertyName("subs")] 
        public List<ItadSubscription>? Subs { get; set; }
    }

    public class ItadSubscription
    {
        public int Id { get; set; } 
        [JsonPropertyName("name")] 
        public string? Name { get; set; } 
        [JsonPropertyName("leaving")] 
        public string? Leaving { get; set; } 
    }

    private string CleanGameTitle(string title)
    {
        title = title.Replace("®", "").Replace("™", "").Replace("©", "");
        title = System.Text.RegularExpressions.Regex.Replace(title, @"[^a-zA-Z0-9\s]", "");
        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
        return title;
    }

    public async Task<List<string>> GetPlainIdsAsync(string title)
    {
        var cleanedTitle = CleanGameTitle(title);
        var encodedTitle = HttpUtility.UrlEncode(cleanedTitle);
        var url = $"{BaseUrl}games/search/v1?key={_apiKey}&title={encodedTitle}&limit=5"; 
        
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<List<ItadGameLookupItem>>(response);

            if (result == null || result.Count == 0)
            {
                return new List<string>();
            }
            
            var potentialMatches = new List<(string plainId, string itadTitle, int score)>();

            foreach (var item in result)
            {
                if (item.Plain == null) continue;
                var cleanedItadTitle = CleanGameTitle(item.Title ?? string.Empty);
                var currentScore = 0;

                // 1. Exact Match
                if (cleanedItadTitle.Equals(cleanedTitle, StringComparison.OrdinalIgnoreCase))
                {
                    currentScore = 100; // Max score for exact match
                }
                // 2. ITAD Title contains Steam title (e.g., "Battlefield 6 Phantom Edition" vs "Battlefield 6")
                else if (cleanedItadTitle.Contains(cleanedTitle, StringComparison.OrdinalIgnoreCase))
                {
                    if (cleanedItadTitle.StartsWith(cleanedTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        currentScore = 80 - Math.Min(40, cleanedItadTitle.Length - cleanedTitle.Length);
                        currentScore = Math.Max(0, currentScore);
                    }
                    else
                    {
                        currentScore = 30 - Math.Min(20, Math.Abs(cleanedItadTitle.Length - cleanedTitle.Length));
                        currentScore = Math.Max(0, currentScore);
                    }
                }
                // 3. Steam Title contains ITAD title (e.g., "Battlefield 6 Ultimate Edition" vs "Battlefield 6")
                else if (cleanedTitle.Contains(cleanedItadTitle, StringComparison.OrdinalIgnoreCase))
                {
                    currentScore = 70 - Math.Min(40, cleanedItadTitle.Length - cleanedItadTitle.Length);
                    currentScore = Math.Max(0, currentScore);
                }
                
                if (currentScore > 0) 
                {
                    potentialMatches.Add((item.Plain, cleanedItadTitle, currentScore));
                }
            }

            // Filter and return all plainIds that meet a reasonable score threshold
            var qualifyingPlainIds = potentialMatches
                .Where(m => m.score >= 50) // Only consider strong matches
                .OrderByDescending(m => m.score)
                .ThenByDescending(m => m.itadTitle.Length) // Prioritize longer titles for editions among same score
                .Select(m => m.plainId)
                .Distinct() // Ensure unique plainIds
                .ToList();

            if (qualifyingPlainIds.Any())
            {
                return qualifyingPlainIds;
            }
            
            return new List<string>();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[Error] ITAD API request failed for '{title}': {ex.Message}");
            return new List<string>();
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Error] Failed to parse ITAD game lookup for '{title}': {ex.Message}");
            return new List<string>();
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
                            if (sub.Leaving == null) 
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
            Console.WriteLine($"[Error] ITAD API POST request failed for plains batch: {ex.Message}");
            foreach (var plainId in plains)
            {
                subscriptionStatus[plainId] = new List<string> { "Error" }; 
            }
            return subscriptionStatus;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Error] Failed to parse ITAD subscription info: {ex.Message}");
            foreach (var plainId in plains)
            {
                subscriptionStatus[plainId] = new List<string> { "Error" }; 
            }
            return subscriptionStatus;
        }
    }
}