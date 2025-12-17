using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class XboxDataService
{
    private readonly string _xboxCookieFilePath;

    public XboxDataService()
    {
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        _xboxCookieFilePath = Path.Combine(authTokensPath, "xbox_cookies.json");
    }

    public async Task<string> GetGamePassSubscriptionDetailsAsync()
    {
        if (!File.Exists(_xboxCookieFilePath))
        {
            Console.WriteLine("Xbox cookies not found. Please run 'legion auth --service xbox' first.");
            return "Error: No Cookies";
        }

        // URL that usually indicates subscription status or a dashboard if logged in
        var subscriptionCheckUrl = "https://www.xbox.com/en-US/live/gold/my-gold-page"; 

        try
        {
            var (pageContent, finalUrl) = await SteamAuthService.FetchProtectedPageAsync(subscriptionCheckUrl, _xboxCookieFilePath);

            // Logic to parse pageContent and determine subscription status
            if (pageContent.Contains("Game Pass Ultimate", StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox Game Pass Ultimate";
            }
            else if (pageContent.Contains("PC Game Pass", StringComparison.OrdinalIgnoreCase))
            {
                return "PC Game Pass";
            }
            else if (pageContent.Contains("Xbox Game Pass for Console", StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox Game Pass for Console";
            }
            else if (pageContent.Contains("Game Pass Core", StringComparison.OrdinalIgnoreCase) || 
                     pageContent.Contains("Xbox Live Gold", StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox Game Pass Core";
            }
            else if (pageContent.Contains("You're a member", StringComparison.OrdinalIgnoreCase))
            {
                return "Active Subscription (Unknown Type)";
            }
            else
            {
                return "None";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to check Xbox Game Pass subscription: {ex.Message}");
            return "Error: Check Failed";
        }
    }
}
