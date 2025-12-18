using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LegionDeck.Core.Services;
using LegionDeck.Core.Models;
using LegionDeck.GUI.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.IO;
using System.Text.Json;
using System.Net.Http;
using System.Collections.Concurrent;

namespace LegionDeck.GUI.Views;

public sealed partial class WishlistPage : Page
{
    private ObservableCollection<SteamWishlistItemViewModel> Wishlist { get; } = new();
    private readonly SteamWishlistService _wishlistService = new();
    private readonly ItadApiService _itadService;
    private readonly SteamGridDbService _sgdbService;
    private readonly ConfigService _configService = new();
    private readonly XboxDataService _xboxData = new();
    private readonly EaDataService _eaData = new();
    private readonly UbisoftDataService _ubisoftData = new();
    private readonly HttpClient _httpClient = new();
    private readonly SteamAuthService _steamAuth = new();
    
    private readonly string _cachePath;

    public WishlistPage()
    {
        this.InitializeComponent();
        _itadService = new ItadApiService(_configService);
        _sgdbService = new SteamGridDbService(_configService);
        
        WishlistGridView.ItemsSource = Wishlist;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cachePath = Path.Combine(appData, "LegionDeck", "Cache", "wishlist_cache.json");
        
        this.Loaded += WishlistPage_Loaded;
    }

    private void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            System.IO.File.AppendAllText(path, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - [WishlistPage] {message}\n");
        }
        catch {{ }}
    }

    private async void WishlistPage_Loaded(object sender, RoutedEventArgs e)
    {
        Log("WishlistPage_Loaded started");
        if (Wishlist.Count == 0)
        {
            await LoadFromCache();
        }
        
        // Give focus to the grid for controller navigation
        await Task.Delay(100);
        WishlistGridView.Focus(FocusState.Programmatic);
        if (Wishlist.Any()) WishlistGridView.SelectedIndex = 0;
    }

    private async Task LoadFromCache()
    {
        if (File.Exists(_cachePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_cachePath);
                var cachedItems = JsonSerializer.Deserialize<List<SteamWishlistItemViewModel>>(json);
                if (cachedItems != null)
                {
                    Wishlist.Clear();
                    foreach (var item in cachedItems)
                    {
                        Wishlist.Add(item);
                    }
                    Log($"Loaded {Wishlist.Count} items from cache");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to load cache: {ex.Message}");
            }
        }
        else
        {
            Log("No cache file found.");
        }
    }

    private async Task SaveToCache(List<SteamWishlistItemViewModel> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var json = JsonSerializer.Serialize(items);
            await File.WriteAllTextAsync(_cachePath, json);
            Log("Saved wishlist to cache");
        }
        catch (Exception ex)
        {
            Log($"Failed to save cache: {ex.Message}");
        }
    }

    private async Task<bool> RemoteImageExists(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _httpClient.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch {{ return false; }}
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        await SyncWishlistAsync();
    }

    private async Task SyncWishlistAsync(bool retryOnAuthFailure = true)
    {
        Log("SyncWishlistAsync started");
        LoadingRing.IsActive = true;
        
        try
        {
            var items = await _wishlistService.GetWishlistAsync();
            Log($"Fetched {items.Count} items from SteamWishlistService");
            
            Wishlist.Clear(); // Clear old items only after successful fetch
            var processedItems = new ConcurrentBag<SteamWishlistItemViewModel>();
            
            var gpStatus = await _xboxData.GetGamePassSubscriptionDetailsAsync();
            bool hasGP = gpStatus.Contains("Ultimate") || gpStatus.Contains("PC");
            
            var eaStatus = await _eaData.GetEaPlaySubscriptionDetailsAsync();
            bool hasEA = eaStatus.Contains("EA Play");

            var ubiStatus = await _ubisoftData.GetUbisoftPlusSubscriptionDetailsAsync();
            bool hasUbi = ubiStatus.Contains("Ubisoft+");

            var semaphore = new SemaphoreSlim(5);
            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var name = await _wishlistService.GetAppDetailsAsync(item.AppId);
                    if (name != null) item.Name = name;

                    var vm = new SteamWishlistItemViewModel(item);
                    
                    bool artFound = false;
                    var sgdbArt = await _sgdbService.GetVerticalCoverAsync(item.AppId);
                    if (!string.IsNullOrEmpty(sgdbArt))
                    {
                        vm.ImgCapsule = sgdbArt;
                        artFound = true;
                    }

                    if (!artFound)
                    {
                        var verticalUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{item.AppId}/library_600x900_2x.jpg";
                        if (await RemoteImageExists(verticalUrl))
                        {
                            vm.ImgCapsule = verticalUrl;
                            artFound = true;
                        }
                    }

                    if (!artFound)
                    {
                        // Fallback: Try searching SGDB by name
                        var gameId = await _sgdbService.SearchGameIdAsync(item.Name ?? $"Steam App {item.AppId}");
                        if (gameId.HasValue)
                        {
                            var coverUrl = await _sgdbService.GetVerticalCoverByGameIdAsync(gameId.Value);
                            if (!string.IsNullOrEmpty(coverUrl))
                            {
                                vm.ImgCapsule = coverUrl;
                                artFound = true;
                            }
                        }
                    }

                    if (!artFound)
                    {
                        vm.ImgCapsule = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{item.AppId}/header.jpg";
                    }

                    var plains = await _itadService.GetPlainIdsAsync(item.Name);
                    if (plains.Any())
                    {
                        var subs = await _itadService.IsOnSubscriptionAsync(plains);
                        var allSubs = subs.Values.SelectMany(x => x).Distinct().ToList();
                        
                        vm.IsOnGamePass = allSubs.Any(s => s.Contains("Game Pass"));
                        vm.IsOnEaPlay = allSubs.Any(s => s.Contains("EA Play"));
                        vm.IsOnUbisoftPlus = allSubs.Any(s => s.Contains("Ubisoft+"));

                        vm.UserHasAccess = (vm.IsOnGamePass && hasGP) || 
                                           (vm.IsOnEaPlay && hasEA) || 
                                           (vm.IsOnUbisoftPlus && hasUbi);
                    }

                    processedItems.Add(vm);
                    DispatcherQueue.TryEnqueue(() => Wishlist.Add(vm));
                }
                finally {{ semaphore.Release(); }}
            });

            await Task.WhenAll(tasks);
            await SaveToCache(processedItems.ToList());
            Log("Sync completed successfully");
        }
        catch (UnauthorizedAccessException)
        {
            // Clear the invalid cookies so we force a fresh extraction
            _steamAuth.ClearCookies();

            if (retryOnAuthFailure)
            {
                Log("Steam session expired. Attempting silent refresh...");
                // Use RefreshSessionAsync to try and renew cookies without showing a window
                var success = await _steamAuth.RefreshSessionAsync();
                if (success)
                {
                    Log("Silent refresh successful. Retrying sync...");
                    await SyncWishlistAsync(false); // Retry once
                }
                else
                {
                     Log("Silent refresh failed. User interaction required (Login in Settings).");
                }
            }
            else
            {
                Log("Steam session expired and retry failed.");
            }
        }
        catch (Exception ex)
        {
            Log($"Error during sync: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if (!retryOnAuthFailure || LoadingRing.IsActive) 
                LoadingRing.IsActive = false;
        }
    }
}
