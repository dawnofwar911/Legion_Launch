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
            var result = JsonSerializer.Deserialize<ItadGameLookupResult>(response);

            if (result == null || result.Count == 0)
            {
                return new List<string>();
            }
            
            // Return all found plains (Simple approach to ensure we catch everything, accepting some false positives like Squad -> Squad 44)
            return result.Where(x => x.Plain != null).Select(x => x.Plain!).ToList();
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