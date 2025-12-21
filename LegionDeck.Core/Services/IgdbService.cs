using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace LegionDeck.Core.Services;

public class IgdbService
{
    private readonly ConfigService _configService;
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private DateTime _tokenExpiry;

    public IgdbService(ConfigService configService)
    {
        _configService = configService;
        _httpClient = new HttpClient();
    }

    private class IgdbAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private class IgdbGame
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private async Task EnsureTokenAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry) return;

        var clientId = _configService.GetApiKey("IGDB_CLIENT_ID");
        var clientSecret = _configService.GetApiKey("IGDB_CLIENT_SECRET");

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return;
        }

        var url = $"https://id.twitch.tv/oauth2/token?client_id={clientId}&client_secret={clientSecret}&grant_type=client_credentials";
        try
        {
            var response = await _httpClient.PostAsync(url, null);
            if (response.IsSuccessStatusCode)
            {
                var auth = await response.Content.ReadFromJsonAsync<IgdbAuthResponse>();
                if (auth != null)
                {
                    _accessToken = auth.AccessToken;
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(auth.ExpiresIn - 60);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IGDB Auth Error: {ex.Message}");
        }
    }

    public async Task<string?> GetGameDescriptionAsync(string gameName)
    {
        await EnsureTokenAsync();

        if (string.IsNullOrEmpty(_accessToken)) return null;

        var clientId = _configService.GetApiKey("IGDB_CLIENT_ID");
        if (string.IsNullOrEmpty(clientId)) return null;

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

        // Search for the game and get the summary
        var sanitizedName = gameName.Replace("\"", "").Replace("'", ""); 
        var query = $"fields name, summary; search \"{sanitizedName}\"; limit 1;";
        request.Content = new StringContent(query);

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var games = await response.Content.ReadFromJsonAsync<List<IgdbGame>>();
                if (games != null && games.Any())
                {
                    return games.First().Summary;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IGDB Fetch Error: {ex.Message}");
        }

        return null;
    }
}
