using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LegionDeck.Core.Services;
using LegionDeck.Core.Models;
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

    private async void WishlistPage_Loaded(object sender, RoutedEventArgs e)
    {
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
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load cache: {ex.Message}");
            }
        }
    }

    private async Task SaveToCache(List<SteamWishlistItemViewModel> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var json = JsonSerializer.Serialize(items);
            await File.WriteAllTextAsync(_cachePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save cache: {ex.Message}");
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
        LoadingRing.IsActive = true;
        Wishlist.Clear();
        var processedItems = new ConcurrentBag<SteamWishlistItemViewModel>();

        try
        {
            var items = await _wishlistService.GetWishlistAsync();
            
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
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            await SaveToCache(processedItems.ToList());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }
}

public class SteamWishlistItemViewModel : SteamWishlistItem
{
    public SteamWishlistItemViewModel() { }

    public SteamWishlistItemViewModel(SteamWishlistItem item)
    {
        this.AppId = item.AppId;
        this.Name = item.Name;
        // Default to Steam vertical art
        this.ImgCapsule = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{item.AppId}/library_600x900_2x.jpg";
    }

    public bool IsOnGamePass { get; set; }
    public bool IsOnEaPlay { get; set; }
    public bool IsOnUbisoftPlus { get; set; }
    public bool UserHasAccess { get; set; }

    public Visibility GamePassVisibility => IsOnGamePass ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EaPlayVisibility => IsOnEaPlay ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UbisoftPlusVisibility => IsOnUbisoftPlus ? Visibility.Visible : Visibility.Collapsed;
    
    public string AccessStatus => UserHasAccess ? "FREE TO PLAY" : "";
    public Visibility AccessVisibility => UserHasAccess ? Visibility.Visible : Visibility.Collapsed;
}