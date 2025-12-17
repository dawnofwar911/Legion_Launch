using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class UbisoftDataService
{
    private readonly string _ubisoftCookieFilePath;

    public UbisoftDataService()
    {
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        _ubisoftCookieFilePath = Path.Combine(authTokensPath, "ubisoft_cookies.json");
    }

    public async Task<string> GetUbisoftPlusSubscriptionDetailsAsync()
    {
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var statusFilePath = Path.Combine(authTokensPath, "ubisoft_status.txt");

        if (File.Exists(statusFilePath))
        {
            try 
            {
                var status = await File.ReadAllTextAsync(statusFilePath);
                Console.WriteLine("[UbisoftDataService] Using cached status from auth scrape.");
                return status.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to read cached status: {ex.Message}");
            }
        }

        if (!File.Exists(_ubisoftCookieFilePath))
        {
            Console.WriteLine("Ubisoft cookies not found. Please run 'legion auth --service ubisoft' first.");
            return "Error: No Cookies";
        }

        // URL that usually indicates subscription status.
        var subscriptionCheckUrl = "https://store.ubisoft.com/uk/my-account"; 

        try
        {
            var (pageContent, finalUrl) = await SteamAuthService.FetchProtectedPageAsync(subscriptionCheckUrl, _ubisoftCookieFilePath);
            
            Console.WriteLine($"[Debug] Final URL: {finalUrl}");

            // Logic to parse pageContent and determine subscription status
            
            // Check for negative indicators first
            if (pageContent.Contains("Subscribe now", StringComparison.OrdinalIgnoreCase) || 
                pageContent.Contains("Join Ubisoft+", StringComparison.OrdinalIgnoreCase) ||
                pageContent.Contains("Choose your plan", StringComparison.OrdinalIgnoreCase))
            {
                return "None";
            }

            // Check for positive indicators
            if (finalUrl.Contains("my-subscription", StringComparison.OrdinalIgnoreCase))
            {
                return "Ubisoft+ Premium";
            }

            if (pageContent.Contains("Manage subscription", StringComparison.OrdinalIgnoreCase) || 
                pageContent.Contains("Next billing", StringComparison.OrdinalIgnoreCase))
            {
                 // Try to distinguish tier if possible
                 if (pageContent.Contains("Classics", StringComparison.OrdinalIgnoreCase)) return "Ubisoft+ Classics";
                 return "Ubisoft+ Premium";
            }

            if (pageContent.Contains("Active", StringComparison.OrdinalIgnoreCase) && 
                (pageContent.Contains("Ubisoft+", StringComparison.OrdinalIgnoreCase) || pageContent.Contains("Ubisoft Plus", StringComparison.OrdinalIgnoreCase)))
            {
                return "Ubisoft+ Premium"; 
            }
            
            return "None";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to check Ubisoft+ subscription: {ex.Message}");
            return "Error: Check Failed";
        }
    }
}
