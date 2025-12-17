using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;
using LegionDeck.CLI.Services;
using System.IO;

namespace LegionDeck.CLI;

public class Program
{
    public class Options
    {
        [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
        public bool Verbose { get; set; }
    }

    [Verb("auth", HelpText = "Authenticate with game services.")]
    public class AuthOptions
    {
        [Option('s', "service", Required = true, HelpText = "Service to authenticate (steam, xbox, all).")]
        public string Service { get; set; } = string.Empty;
    }

    [Verb("sync", HelpText = "Synchronize data from game services.")]
    public class SyncOptions
    {
        [Option('w', "wishlist", Required = false, HelpText = "Synchronize wishlist data.")]
        public bool Wishlist { get; set; }

        // Add other sync options here as needed, e.g., --library
    }

    [Verb("config", HelpText = "Configure application settings.")]
    public class ConfigOptions
    {
        [Option("set-api-key", Required = false, HelpText = "Sets API keys (ITAD, IGDB). Format: --set-api-key ITAD=<your_key>")]
        public string? SetApiKey { get; set; }

        // Future config options can be added here
    }

    static async Task Main(string[] args)
    {
        using var host = CreateHostBuilder(args).Build();

        await Parser.Default.ParseArguments<Options, AuthOptions, SyncOptions, ConfigOptions>(args)
            .MapResult(
                (Options opts) => RunOptions(opts),
                (AuthOptions opts) => RunAuthAndReturnExitCode(opts, host.Services),
                (SyncOptions opts) => RunSyncAndReturnExitCode(opts, host.Services),
                (ConfigOptions opts) => RunConfigAndReturnExitCode(opts, host.Services),
                errs => Task.FromResult(1)
            );
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                services.AddTransient<SteamAuthService>();
                services.AddTransient<XboxAuthService>();
                services.AddTransient<SteamWishlistService>(); 
                services.AddSingleton<ConfigService>(); // ConfigService is a singleton as it manages persistent state
                services.AddTransient<ItadApiService>(); // Register ItadApiService
            });

    static Task<int> RunOptions(Options opts)
    {
        Console.WriteLine("LegionDeck CLI");
        return Task.FromResult(0);
    }

    static async Task<int> RunAuthAndReturnExitCode(AuthOptions opts, IServiceProvider services)
    {
        Console.WriteLine($"Authenticating service: {opts.Service}");
        
        if (opts.Service.Equals("steam", StringComparison.OrdinalIgnoreCase))
        {
             var steamAuth = services.GetRequiredService<SteamAuthService>();
             try 
             {
                 var cookie = await steamAuth.LoginAsync();
                 if (!string.IsNullOrEmpty(cookie))
                 {
                    Console.WriteLine("Steam authentication completed.");
                 }
                 else
                 {
                    Console.WriteLine("Steam authentication failed: No cookie retrieved.");
                    return 1;
                 }
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"Steam authentication failed: {ex.Message}");
                 return 1;
             }
        }
        else if (opts.Service.Equals("xbox", StringComparison.OrdinalIgnoreCase))
        {
             var xboxAuth = services.GetRequiredService<XboxAuthService>();
             try
             {
                 var status = await xboxAuth.LoginAsync();
                 if (status == "XboxLoggedIn")
                 {
                    Console.WriteLine("Xbox authentication completed.");
                 }
                 else
                 {
                    Console.WriteLine("Xbox authentication failed.");
                    return 1;
                 }
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"Xbox authentication failed: {ex.Message}");
                 return 1;
             }
        }
        else
        {
             Console.WriteLine("Unknown service.");
             return 1;
        }

        return 0;
    }

    static async Task<int> RunSyncAndReturnExitCode(SyncOptions opts, IServiceProvider services)
    {
        if (opts.Wishlist)
        {
            Console.WriteLine("Synchronizing wishlist...");
            
            // Check for Steam cookie file existence
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var steamCookieFilePath = Path.Combine(authTokensPath, "steam_cookies.json");

            if (!File.Exists(steamCookieFilePath))
            {
                Console.WriteLine("Steam cookies not found. Please run 'legion auth --service steam' first.");
                return 1;
            }

            var steamWishlistService = new SteamWishlistService(); 
            var itadService = services.GetRequiredService<ItadApiService>();
            
            // Pass null to trigger auto-detection of the logged-in user's profile
            var wishlistItems = await steamWishlistService.GetWishlistAsync(null);

            Console.WriteLine($"Found {wishlistItems.Count} items in Steam Wishlist:");

            var gamePlainIds = new List<string>();

            foreach (var item in wishlistItems)
            {
                var gameName = await steamWishlistService.GetAppDetailsAsync(item.AppId);
                if (gameName == null)
                {
                    gameName = $"AppID {item.AppId}"; // Fallback to AppID if name not found
                }
                item.Name = gameName; // Update item with actual name

                var plainId = await itadService.GetPlainIdAsync(item.Name);
                if (plainId != null)
                {
                    item.PlainId = plainId; // Store PlainId in the item
                    gamePlainIds.Add(plainId);
                }
            }

            // Batched call to check subscription status
            var subscriptionStatuses = await itadService.IsOnSubscriptionAsync(gamePlainIds);

            foreach (var item in wishlistItems)
            {
                string statusMessage;

                if (item.PlainId != null && subscriptionStatuses.TryGetValue(item.PlainId, out List<string>? activeSubs) && activeSubs != null && activeSubs.Any())
                {
                    statusMessage = $"On: {string.Join(", ", activeSubs)}";
                }
                else
                {
                    statusMessage = "Not on any subscription.";
                }
                
                Console.WriteLine($"- {item.Name} (AppId: {item.AppId}) - Status: {statusMessage}");
            }
        }
        return 0;
    }

    static Task<int> RunConfigAndReturnExitCode(ConfigOptions opts, IServiceProvider services)
    {
        Console.WriteLine("Configuring application settings...");
        if (!string.IsNullOrEmpty(opts.SetApiKey))
        {
            var parts = opts.SetApiKey.Split('=', 2); // Split only on the first '='
            if (parts.Length == 2)
            {
                var serviceName = parts[0].Trim();
                var apiKey = parts[1].Trim();
                if (!string.IsNullOrEmpty(serviceName) && !string.IsNullOrEmpty(apiKey))
                {
                    var configService = services.GetRequiredService<ConfigService>();
                    configService.SetApiKey(serviceName, apiKey);
                    return Task.FromResult(0);
                }
            }
            Console.WriteLine("Invalid API key format. Use: --set-api-key SERVICE_NAME=<YOUR_KEY>");
            return Task.FromResult(1);
        }
        Console.WriteLine("No config action specified.");
        return Task.FromResult(1);
    }
}
