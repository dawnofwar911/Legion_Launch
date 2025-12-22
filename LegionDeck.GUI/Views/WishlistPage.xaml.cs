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
    private readonly MetadataService _metadataService;
    private readonly GameEnrichmentService _enrichmentService;
    
    private readonly string _cachePath;

    public WishlistPage()
    {
        this.InitializeComponent();
        _itadService = new ItadApiService(_configService);
        _sgdbService = new SteamGridDbService(_configService);
        _metadataService = new MetadataService();
        _enrichmentService = new GameEnrichmentService(_configService, _metadataService);
        
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
        catch { }
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back)
        {
            // Restore focus to grid when coming back
            await Task.Delay(100);
            WishlistGridView.Focus(FocusState.Programmatic);
        }
    }

    private async void WishlistPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log("WishlistPage_Loaded started");
            await LoadFromCache();
            
            if (Wishlist.Count == 0)
            {
                Log("Cache empty or not found.");
                _ = SyncWishlistAsync();
            }
            
            // Focus item container
            if (Wishlist.Any())
            {
                if (WishlistGridView.SelectedIndex < 0) WishlistGridView.SelectedIndex = 0;
                
                await Task.Delay(100);
                var container = WishlistGridView.ContainerFromIndex(WishlistGridView.SelectedIndex) as Control;
                container?.Focus(FocusState.Programmatic);
            }
        }
        catch (Exception ex)
        {
            Log($"Error loading wishlist: {ex.Message}");
        }
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
        catch { return false; }
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        await SyncWishlistAsync();
    }

    private void SortByName_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item)
        {
            var list = Wishlist.ToList();
            if (item.Tag?.ToString() == "NameAsc")
                list = list.OrderBy(g => g.Name).ToList();
            else
                list = list.OrderByDescending(g => g.Name).ToList();

            Wishlist.Clear();
            foreach (var g in list) Wishlist.Add(g);
        }
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

                    if (!string.IsNullOrEmpty(item.Name))
                    {
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
                    }

                    processedItems.Add(vm);
                    DispatcherQueue.TryEnqueue(() => Wishlist.Add(vm));
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            await SaveToCache(processedItems.ToList());
            Log("Sync completed successfully");
            
            // Start background enrichment
            _ = EnrichWishlistAsync(processedItems.ToList());
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

    private async Task EnrichWishlistAsync(List<SteamWishlistItemViewModel> items)
    {
        Log("Starting background enrichment...");
        foreach (var item in items)
        {
            try
            {
                // We pass "Steam" as source because wishlist items are by definition from Steam
                await _enrichmentService.EnrichGameAsync(item.AppId.ToString(), item.Name, "Steam");
                await Task.Delay(250); // Be polite
            }
            catch (Exception ex)
            {
                Log($"Enrichment failed for {item.Name}: {ex.Message}");
            }
        }
        Log("Background enrichment completed.");
    }

    private void WishlistGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SteamWishlistItemViewModel vm)
        {
            Log($"Wishlist item clicked: {vm.Name}. Navigating to details.");
            this.Frame.Navigate(typeof(GameDetailsPage), vm);
        }
    }

    private void WishlistGridView_GettingFocus(UIElement sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        // If the focus is moving TO the GridView itself (not an item inside it)
        // We redirect it to the selected item or first item.
        if (args.NewFocusedElement == WishlistGridView && WishlistGridView.Items.Count > 0)
        {
            if (WishlistGridView.SelectedIndex < 0) WishlistGridView.SelectedIndex = 0;
            
            var container = WishlistGridView.ContainerFromIndex(WishlistGridView.SelectedIndex) as Control;
            if (container != null)
            {
                args.TrySetNewFocusedElement(container);
                args.Handled = true;
            }
        }
    }

    private void WishlistGridView_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Add specific wishlist gamepad logic here if needed (e.g. Y for refresh)
        if (e.Key == Windows.System.VirtualKey.GamepadY || e.Key == Windows.System.VirtualKey.Y)
        {
             _ = SyncWishlistAsync();
             e.Handled = true;
        }
    }
}
