using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class EaDataService
{
    private readonly string _eaCookieFilePath;

    public EaDataService()
    {
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        _eaCookieFilePath = Path.Combine(authTokensPath, "ea_cookies.json");
    }

    public async Task<string> GetEaPlaySubscriptionDetailsAsync()
    {
        if (!File.Exists(_eaCookieFilePath))
        {
            Console.WriteLine("EA cookies not found. Please run 'legion auth --service ea' first.");
            return "Error: No Cookies";
        }

        // URL that often displays EA Play subscription status
        var subscriptionCheckUrl = "https://www.ea.com/ea-play/member-benefits"; 

        try
        {
            var (pageContent, finalUrl) = await SteamAuthService.FetchProtectedPageAsync(subscriptionCheckUrl, _eaCookieFilePath);

            // Logic to parse pageContent and determine subscription status
            // This is highly dependent on the structure of the EA website.
            // For now, a simple string contains check will be used as a POC.

            if (pageContent.Contains("EA Play Pro", StringComparison.OrdinalIgnoreCase))
            {
                return "EA Play Pro";
            }
            else if (pageContent.Contains("EA Play", StringComparison.OrdinalIgnoreCase) && !pageContent.Contains("EA Play Pro", StringComparison.OrdinalIgnoreCase))
            {
                // Check for generic EA Play, excluding Pro if it wasn't caught above
                return "EA Play";
            }
            else
            {
                return "None";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to check EA Play subscription: {ex.Message}");
            return "Error: Check Failed";
        }
    }
}
