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
            File.AppendAllText(path, "{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [XboxDataService] {message}\n");
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

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            client.DefaultRequestHeaders.Add("x-xbl-contract-version", "2");
            client.DefaultRequestHeaders.Add("Authorization", auth.AuthHeader);
            client.DefaultRequestHeaders.Add("Accept-Language", "en-GB");

            // 1. Try the core profile settings API
            // These specific settings are known to return subscription-related strings
            var profileUrl = $"https://profile.xboxlive.com/users/xuid({auth.Xuid})/profile/settings?settings=AccountTier,TenureLevel,GameDisplayName,AppDisplayName,AppDisplayPicRaw"; 
            var response = await client.GetAsync(profileUrl);
            var json = await response.Content.ReadAsStringAsync();

            Log($"Profile API Check. Length: {json.Length}");

            // Scrutinize the JSON for ANY subscription indicators
            if (json.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)) return "Xbox Game Pass Ultimate";
            if (json.Contains("PC", StringComparison.OrdinalIgnoreCase) && json.Contains("GamePass", StringComparison.OrdinalIgnoreCase)) return "PC Game Pass";
            
            // 2. Try scraping the UK legacy page as fallback (known to work for some UK accounts)
            var subscriptionCheckUrl = "https://www.xbox.com/en-GB/live/gold/my-gold-page"; 
            var scrapeResponse = await client.GetAsync(subscriptionCheckUrl);
            var content = await scrapeResponse.Content.ReadAsStringAsync();

            if (content.Contains("Game Pass Ultimate", StringComparison.OrdinalIgnoreCase) || content.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox Game Pass Ultimate";
            }
            else if (content.Contains("PC Game Pass", StringComparison.OrdinalIgnoreCase))
            {
                return "PC Game Pass";
            }
            else if (content.Contains("Game Pass Core", StringComparison.OrdinalIgnoreCase) || content.Contains("Xbox Live Gold", StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox Game Pass Core";
            }

            Log("Subscription detection yielded 'None' after all checks.");
            return "None";
        }
        catch (Exception ex)
        {
            Log($"Subscription Check Exception: {ex.Message}");
            return "Error";
        }
    }
}
