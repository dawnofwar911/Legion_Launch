using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace LegionDeck.Core.Services;

public class SteamStoreService
{
    private readonly HttpClient _httpClient;

    public SteamStoreService()
    {
        _httpClient = new HttpClient();
    }

    public class SteamStoreDetails
    {
        public string Name { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string HeaderImage { get; set; } = string.Empty;
    }

    public async Task<SteamStoreDetails?> GetStoreDetailsAsync(int appId)
    {
        var url = $"https://store.steampowered.com/api/appdetails/?appids={appId}&cc=us&l=en";
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            
            if (doc.RootElement.TryGetProperty(appId.ToString(), out var appElement) &&
                appElement.TryGetProperty("success", out var successElement) &&
                successElement.GetBoolean() == true &&
                appElement.TryGetProperty("data", out var dataElement))
            {
                var details = new SteamStoreDetails();
                
                if (dataElement.TryGetProperty("name", out var nameElement))
                    details.Name = nameElement.GetString() ?? string.Empty;
                
                if (dataElement.TryGetProperty("short_description", out var descElement))
                    details.ShortDescription = StripHtml(descElement.GetString() ?? string.Empty);

                if (dataElement.TryGetProperty("header_image", out var imgElement))
                    details.HeaderImage = imgElement.GetString() ?? string.Empty;

                return details;
            }
        }
        catch (Exception ex)
        {
            // Log error?
            System.Diagnostics.Debug.WriteLine($"Error fetching store details: {ex.Message}");
        }
        return null;
    }

    private string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        
        // Decode HTML entities
        var decoded = System.Net.WebUtility.HtmlDecode(input);
        
        // Remove tags
        return Regex.Replace(decoded, "<.*?>", string.Empty);
    }
}
