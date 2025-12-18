using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Linq;

namespace LegionDeck.Core.Services;

public class SteamGridDbService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigService _configService;
    private string? _apiKey;
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2/";

    public SteamGridDbService(ConfigService configService)
    {
        _configService = configService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LegionDeck/1.0");
    }

    private void EnsureApiKey()
    {
        _apiKey ??= _configService.GetApiKey("SGDB");
    }

    public class SgdbResponse<T>
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    public class SgdbImage
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        [JsonPropertyName("thumb")]
        public string? Thumb { get; set; }
        [JsonPropertyName("score")]
        public int Score { get; set; }
    }

    public class SgdbGame
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            System.IO.File.AppendAllText(path, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SGDB] {message}\n");
        }
        catch { }
    }

    public async Task<int?> SearchGameIdAsync(string gameName)
    {
        EnsureApiKey();
        if (string.IsNullOrEmpty(_apiKey)) 
        {
            Log("API Key is missing or empty.");
            return null;
        }

        var url = $"{BaseUrl}search/autocomplete/{Uri.EscapeDataString(gameName)}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) 
            {
                Log($"Search failed for '{gameName}'. Status: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SgdbResponse<List<SgdbGame>>>(json);

            if (result?.Success == true && result.Data != null && result.Data.Any())
            {
                var id = result.Data.First().Id;
                Log($"Found ID {id} for game '{gameName}'");
                return id;
            }
            else
            {
                Log($"No results found for '{gameName}'");
            }
        }
        catch (Exception ex)
        {
            Log($"Search Error for '{gameName}': {ex.Message}");
        }
        return null;
    }

    public async Task<string?> GetVerticalCoverByGameIdAsync(int gameId)
    {
        EnsureApiKey();
        if (string.IsNullOrEmpty(_apiKey)) return null;

        var url = $"{BaseUrl}grids/game/{gameId}?dimensions=600x900,342x482,660x930";
        
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log($"GetGrid failed for ID {gameId}. Status: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SgdbResponse<List<SgdbImage>>>(json);

            if (result?.Success == true && result.Data != null && result.Data.Any())
            {
                var urlResult = result.Data.OrderByDescending(x => x.Score).FirstOrDefault()?.Url;
                Log($"Found cover for ID {gameId}: {urlResult}");
                return urlResult;
            }
            else
            {
                Log($"No cover found for ID {gameId}");
            }
        }
        catch (Exception ex)
        {
            Log($"GetGrid Error for ID {gameId}: {ex.Message}");
        }

        return null;
    }

    public async Task<string?> GetVerticalCoverAsync(int steamAppId)
    {
        EnsureApiKey();
        if (string.IsNullOrEmpty(_apiKey)) return null;

        var url = $"{BaseUrl}grids/steam/{steamAppId}?dimensions=600x900,342x482,660x930";
        
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SgdbResponse<List<SgdbImage>>>(json);

            if (result?.Success == true && result.Data != null && result.Data.Any())
            {
                // Return the highest scored image
                return result.Data.OrderByDescending(x => x.Score).FirstOrDefault()?.Url;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SGDB Error: {ex.Message}");
        }

        return null;
    }
}
