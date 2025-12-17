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

namespace LegionDeck.GUI.Views;

public sealed partial class WishlistPage : Page
{
    private ObservableCollection<SteamWishlistItemViewModel> Wishlist { get; } = new();
    private readonly SteamWishlistService _wishlistService = new();
    private readonly ItadApiService _itadService;
    private readonly ConfigService _configService = new();
    private readonly XboxDataService _xboxData = new();
    private readonly EaDataService _eaData = new();
    private readonly UbisoftDataService _ubisoftData = new();

    public WishlistPage()
    {
        this.InitializeComponent();
        _itadService = new ItadApiService(_configService);
        WishlistGridView.ItemsSource = Wishlist;
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        LoadingRing.IsActive = true;
        Wishlist.Clear();

        try
        {
            // 1. Fetch Basic Wishlist
            var items = await _wishlistService.GetWishlistAsync();
            
            // 2. Determine User Subscription Access
            var gpStatus = await _xboxData.GetGamePassSubscriptionDetailsAsync();
            bool hasGP = gpStatus.Contains("Ultimate") || gpStatus.Contains("PC");
            
            var eaStatus = await _eaData.GetEaPlaySubscriptionDetailsAsync();
            bool hasEA = eaStatus.Contains("EA Play");

            var ubiStatus = await _ubisoftData.GetUbisoftPlusSubscriptionDetailsAsync();
            bool hasUbi = ubiStatus.Contains("Ubisoft+");

            // 3. Process items (Limited concurrency for ITAD)
            var semaphore = new SemaphoreSlim(5);
            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Resolve Name
                    var name = await _wishlistService.GetAppDetailsAsync(item.AppId);
                    if (name != null) item.Name = name;

                    var vm = new SteamWishlistItemViewModel(item);
                    
                    // Resolve Subscriptions via ITAD
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

                    // Update UI on main thread
                    DispatcherQueue.TryEnqueue(() => Wishlist.Add(vm));
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
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
    public SteamWishlistItemViewModel(SteamWishlistItem item)
    {
        this.AppId = item.AppId;
        this.Name = item.Name;
        this.ImgCapsule = $"https://cdn.akamai.steamstatic.com/steam/apps/{item.AppId}/header.jpg";
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
