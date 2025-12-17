using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;
using LegionDeck.Core.Services;
using LegionDeck.Core.Models;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

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
        [Option('s', "service", Required = true, HelpText = "Service to authenticate (steam, xbox, ea, ubisoft, all).")]
        public string Service { get; set; } = string.Empty;
    }

    [Verb("sync", HelpText = "Synchronize data from game services.")]
    public class SyncOptions
    {
        [Option('w', "wishlist", Required = false, HelpText = "Synchronize wishlist data.")]
        public bool Wishlist { get; set; }

        [Option('g', "gamepass", Required = false, HelpText = "Check Xbox Game Pass subscription status.")]
        public bool GamePass { get; set; }

        [Option('e', "eaplay", Required = false, HelpText = "Check EA Play subscription status.")]
        public bool EaPlay { get; set; }

        [Option('u', "ubisoftplus", Required = false, HelpText = "Check Ubisoft+ subscription status.")]
        public bool UbisoftPlus { get; set; }
    }

    [Verb("config", HelpText = "Configure application settings.")]
    public class ConfigOptions
    {
        [Option("set-api-key", Required = false, HelpText = "Sets API keys (ITAD, IGDB). Format: --set-api-key ITAD=<your_key>")]
        public string? SetApiKey { get; set; }
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
                services.AddTransient<EaAuthService>();
                services.AddTransient<UbisoftAuthService>();
                services.AddTransient<SteamWishlistService>(); 
                services.AddTransient<XboxDataService>(); 
                services.AddTransient<EaDataService>(); 
                services.AddTransient<UbisoftDataService>();
                services.AddSingleton<ConfigService>(); 
                services.AddTransient<ItadApiService>(); 
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
        else if (opts.Service.Equals("ea", StringComparison.OrdinalIgnoreCase))
        {
             var eaAuth = services.GetRequiredService<EaAuthService>();
             try
             {
                 var status = await eaAuth.LoginAsync();
                 if (status == "EALoggedIn")
                 {
                    Console.WriteLine("EA authentication completed.");
                 }
                 else
                 {
                    Console.WriteLine("EA authentication failed.");
                    return 1;
                 }
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"EA authentication failed: {ex.Message}");
                 return 1;
             }
        }
        else if (opts.Service.Equals("ubisoft", StringComparison.OrdinalIgnoreCase))
        {
             var ubiAuth = services.GetRequiredService<UbisoftAuthService>();
             try
             {
                 var status = await ubiAuth.LoginAsync();
                 if (status == "UbisoftLoggedIn")
                 {
                    Console.WriteLine("Ubisoft authentication completed.");
                 }
                 else
                 {
                    Console.WriteLine("Ubisoft authentication failed.");
                    return 1;
                 }
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"Ubisoft authentication failed: {ex.Message}");
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
            
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var steamCookieFilePath = Path.Combine(authTokensPath, "steam_cookies.json");

            if (!File.Exists(steamCookieFilePath))
            {
                Console.WriteLine("Steam cookies not found. Please run 'legion auth --service steam' first.");
                return 1;
            }

            var steamWishlistService = new SteamWishlistService(); 
            var itadService = services.GetRequiredService<ItadApiService>();
            
            var wishlistItems = await steamWishlistService.GetWishlistAsync(null);

            if (wishlistItems.Count == 0)
            {
                 Console.WriteLine("Wishlist appears empty or session is stale. Attempting to refresh session...");
                 var steamAuth = services.GetRequiredService<SteamAuthService>();
                 var refreshed = await steamAuth.RefreshSessionAsync();
                 if (refreshed)
                 {
                     Console.WriteLine("Session refreshed. Retrying sync...");
                     wishlistItems = await steamWishlistService.GetWishlistAsync(null);
                 }
                 else
                 {
                     Console.WriteLine("Session refresh failed. Please run 'legion auth --service steam' to login interactively.");
                 }
            }

            Console.WriteLine($"Found {wishlistItems.Count} items in Steam Wishlist:");

            var gamePlainIds = new List<string>();
            var itemProcessingTasks = new List<Task>();
            var semaphore = new SemaphoreSlim(5); 

            foreach (var item in wishlistItems)
            {
                itemProcessingTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(); 
                    try
                    {
                        var gameName = await steamWishlistService.GetAppDetailsAsync(item.AppId);
                        if (gameName == null)
                        {
                            gameName = $"AppID {item.AppId}"; 
                        }
                        item.Name = gameName; 

                        var plainIds = await itadService.GetPlainIdsAsync(item.Name);
                        if (plainIds != null && plainIds.Any())
                        {
                            item.PlainIds.AddRange(plainIds); 
                        }
                    }
                    finally
                    {
                        semaphore.Release(); 
                    }
                }));
            }


            await Task.WhenAll(itemProcessingTasks);

            foreach (var item in wishlistItems)
            {
                if (item.PlainIds.Any())
                {
                    gamePlainIds.AddRange(item.PlainIds);
                }
            }

            // Batched call to check subscription status
            var subscriptionStatuses = new Dictionary<string, List<string>>();
            if (gamePlainIds.Count > 0)
            {
                // Remove duplicates and invalid IDs to minimize API load and errors
                gamePlainIds = gamePlainIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
                
                const int batchSize = 20;
                for (int i = 0; i < gamePlainIds.Count; i += batchSize)
                {
                    var batch = gamePlainIds.Skip(i).Take(batchSize).ToList();
                    try 
                    {
                        var batchResults = await itadService.IsOnSubscriptionAsync(batch);
                        foreach (var kvp in batchResults)
                        {
                            subscriptionStatuses[kvp.Key] = kvp.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] Failed to fetch subscription status for batch starting at index {i}: {ex.Message}");
                    }
                }
            }

            // Check User's Game Pass Status
            var xboxDataService = services.GetRequiredService<XboxDataService>();
            var userGamePassStatus = await xboxDataService.GetGamePassSubscriptionDetailsAsync();
            bool userHasGamePassAccess = userGamePassStatus.Contains("Ultimate", StringComparison.OrdinalIgnoreCase) || 
                                         userGamePassStatus.Contains("PC", StringComparison.OrdinalIgnoreCase);

            // Check User's EA Play Status
            var eaDataService = services.GetRequiredService<EaDataService>();
            var userEaPlayStatus = await eaDataService.GetEaPlaySubscriptionDetailsAsync();
            bool userHasEaPlayAccess = userEaPlayStatus.Contains("EA Play", StringComparison.OrdinalIgnoreCase);

            // Check User's Ubisoft+ Status
            var ubisoftDataService = services.GetRequiredService<UbisoftDataService>();
            var userUbisoftPlusStatus = await ubisoftDataService.GetUbisoftPlusSubscriptionDetailsAsync();
            bool userHasUbisoftPlusAccess = userUbisoftPlusStatus.Contains("Ubisoft+", StringComparison.OrdinalIgnoreCase);


            if (userHasGamePassAccess)
            {
                Console.WriteLine($"[Subscription Check] Active: {userGamePassStatus} - Checking for free games...");
            }
            if (userHasEaPlayAccess)
            {
                Console.WriteLine($"[Subscription Check] Active: {userEaPlayStatus} - Checking for free games...");
            }
            if (userHasUbisoftPlusAccess)
            {
                Console.WriteLine($"[Subscription Check] Active: {userUbisoftPlusStatus} - Checking for free games...");
            }


            foreach (var item in wishlistItems)
            {
                string statusMessage;
                
                var activeSubs = new List<string>();
                foreach (var pid in item.PlainIds)
                {
                    if (subscriptionStatuses.TryGetValue(pid, out var subs) && subs != null)
                    {
                        activeSubs.AddRange(subs);
                    }
                }
                activeSubs = activeSubs.Distinct().ToList();

                if (activeSubs.Any())
                {
                    bool isOnGamePass = activeSubs.Any(s => s.Contains("Game Pass", StringComparison.OrdinalIgnoreCase));
                    bool isOnEaPlay = activeSubs.Any(s => s.Contains("EA Play", StringComparison.OrdinalIgnoreCase));
                    bool isOnUbisoftPlus = activeSubs.Any(s => s.Contains("Ubisoft+", StringComparison.OrdinalIgnoreCase) || s.Contains("Ubisoft Plus", StringComparison.OrdinalIgnoreCase));
                    
                    if (isOnGamePass)
                    {
                        if (userHasGamePassAccess)
                        {
                            statusMessage = "*** FREE via Game Pass! ***";
                        }
                        else
                        {
                            statusMessage = "Available on Game Pass (Subscription required)";
                        }
                        
                        var otherSubs = activeSubs.Where(s => !s.Contains("Game Pass", StringComparison.OrdinalIgnoreCase) && !s.Contains("EA Play", StringComparison.OrdinalIgnoreCase) && !s.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase)).ToList();
                        
                        if (isOnEaPlay && userHasEaPlayAccess) statusMessage += " & EA Play!";
                        if (isOnUbisoftPlus && userHasUbisoftPlusAccess) statusMessage += " & Ubisoft+!";
                        
                        if (otherSubs.Any())
                        {
                            statusMessage += $" (Also on: {string.Join(", ", otherSubs)})";
                        }
                    }
                    else if (isOnEaPlay)
                    {
                        if (userHasEaPlayAccess)
                        {
                            statusMessage = "*** FREE via EA Play! ***";
                        }
                        else
                        {
                            statusMessage = "Available on EA Play (Subscription required)";
                        }

                        if (isOnUbisoftPlus && userHasUbisoftPlusAccess) statusMessage += " & Ubisoft+!";

                        var otherSubs = activeSubs.Where(s => !s.Contains("EA Play", StringComparison.OrdinalIgnoreCase) && !s.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (otherSubs.Any())
                        {
                            statusMessage += $" (Also on: {string.Join(", ", otherSubs)})";
                        }
                    }
                    else if (isOnUbisoftPlus)
                    {
                        if (userHasUbisoftPlusAccess)
                        {
                            statusMessage = "*** FREE via Ubisoft+! ***";
                        }
                        else
                        {
                            statusMessage = "Available on Ubisoft+ (Subscription required)";
                        }
                        
                        var otherSubs = activeSubs.Where(s => !s.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (otherSubs.Any())
                        {
                            statusMessage += $" (Also on: {string.Join(", ", otherSubs)})";
                        }
                    }
                    else
                    {
                        statusMessage = $"On: {string.Join(", ", activeSubs)}";
                    }
                }
                else
                {
                    statusMessage = "Not on any subscription.";
                }
                
                Console.WriteLine($"- {item.Name} (AppId: {item.AppId}) - Status: {statusMessage}");
            }
        }
        else if (opts.GamePass)
        {
            Console.WriteLine("Checking Xbox Game Pass subscription status...");
            var xboxDataService = services.GetRequiredService<XboxDataService>();
            var subscriptionType = await xboxDataService.GetGamePassSubscriptionDetailsAsync();

            if (subscriptionType.StartsWith("Error"))
            {
                Console.WriteLine($"Could not determine subscription status. {subscriptionType}");
            }
            else if (subscriptionType != "None")
            {
                Console.WriteLine($"You have an active subscription: {subscriptionType}");
            }
            else
            {
                Console.WriteLine("No active Xbox Game Pass subscription detected.");
            }
        }
        else if (opts.EaPlay)
        {
            Console.WriteLine("Checking EA Play subscription status...");
            var eaDataService = services.GetRequiredService<EaDataService>();
            var subscriptionType = await eaDataService.GetEaPlaySubscriptionDetailsAsync();

            if (subscriptionType.StartsWith("Error"))
            {
                Console.WriteLine($"Could not determine subscription status. {subscriptionType}");
            }
            else if (subscriptionType != "None")
            {
                Console.WriteLine($"You have an active subscription: {subscriptionType}");
            }
            else
            {
                Console.WriteLine("No active EA Play subscription detected.");
            }
        }
        else if (opts.UbisoftPlus)
        {
            Console.WriteLine("Checking Ubisoft+ subscription status...");
            var ubisoftDataService = services.GetRequiredService<UbisoftDataService>();
            var subscriptionType = await ubisoftDataService.GetUbisoftPlusSubscriptionDetailsAsync();

            if (subscriptionType.StartsWith("Error"))
            {
                Console.WriteLine($"Could not determine subscription status. {subscriptionType}");
            }
            else if (subscriptionType != "None")
            {
                Console.WriteLine($"You have an active subscription: {subscriptionType}");
            }
            else
            {
                Console.WriteLine("No active Ubisoft+ subscription detected.");
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