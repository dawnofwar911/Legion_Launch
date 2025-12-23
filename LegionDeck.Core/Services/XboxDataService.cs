using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;

namespace LegionDeck.Core.Services;

public class XboxDataService
{
    public XboxDataService()
    {
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [XboxDataService] {message}\n");
        }
        catch {{ }}
    }

    public async Task<string> GetGamePassSubscriptionDetailsAsync()
    {
        var authService = new XboxAuthService();
        var auth = await authService.GetXstsTokenAsync();

        if (string.IsNullOrEmpty(auth.AuthHeader))
        {
            return "None (Login Required)";
        }

        // Use the legacy URL that we know used to work for scraping
        var subscriptionCheckUrl = "https://www.xbox.com/en-US/live/gold/my-gold-page"; 

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            client.DefaultRequestHeaders.Add("x-xbl-contract-version", "2");
            client.DefaultRequestHeaders.Add("Authorization", auth.AuthHeader);
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US");

            var response = await client.GetAsync(subscriptionCheckUrl);
            var content = await response.Content.ReadAsStringAsync();

            Log($"Scrape check performed on {subscriptionCheckUrl}. Length: {content.Length}");

            // Re-implement the original successful scraping logic
            if (content.Contains("Game Pass Ultimate", StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox Game Pass Ultimate";
            }
            else if (content.Contains("PC Game Pass", StringComparison.OrdinalIgnoreCase))
            {
                return "PC Game Pass";
            }
            else if (content.Contains("Xbox Game Pass for Console", StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox Game Pass for Console";
            }
            else if (content.Contains("Game Pass Core", StringComparison.OrdinalIgnoreCase) || 
                     content.Contains("Xbox Live Gold", StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox Game Pass Core";
            }
            else if (content.Contains("You're a member", StringComparison.OrdinalIgnoreCase))
            {
                return "Active Subscription (Unknown Type)";
            }
            
            // Fallback: If scraping failed, try the Profile JSON again
            var profileUrl = $"https://profile.xboxlive.com/users/xuid({auth.Xuid})/profile/settings?settings=AccountTier,TenureLevel,SubscriptionTier";
            var profileResponse = await client.GetAsync(profileUrl);
            var profileJson = await profileResponse.Content.ReadAsStringAsync();
            
            if (profileJson.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)) return "Xbox Game Pass Ultimate";
            if (profileJson.Contains("PC", StringComparison.OrdinalIgnoreCase)) return "PC Game Pass";

            return "None";
        }
        catch (Exception ex)
        {
            Log($"Failed to check Xbox Game Pass subscription: {ex.Message}");
            return "Error";
        }
    }
}
