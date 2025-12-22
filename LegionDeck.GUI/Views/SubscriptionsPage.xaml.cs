using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LegionDeck.Core.Services;
using LegionDeck.GUI.Models;
using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;

namespace LegionDeck.GUI.Views;

public sealed partial class SubscriptionsPage : Page
{
    private readonly XboxDataService _xboxData = new();
    private readonly EaDataService _eaData = new();
    private readonly UbisoftDataService _ubisoftData = new();
    private ObservableCollection<SteamWishlistItemViewModel> Matches { get; } = new();
    
    private readonly string _wishlistCachePath;

    public SubscriptionsPage()
    {
        this.InitializeComponent();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _wishlistCachePath = Path.Combine(appData, "LegionDeck", "Cache", "wishlist_cache.json");
        
        MatchesGridView.ItemsSource = Matches;
        this.Loaded += SubscriptionsPage_Loaded;
    }

    private async void SubscriptionsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshDashboard();
    }

    private async Task RefreshDashboard()
    {
        LoadingRing.IsActive = true;
        NoMatchesText.Visibility = Visibility.Collapsed;

        // 1. Refresh Status
        var xboxTask = _xboxData.GetGamePassSubscriptionDetailsAsync();
        var eaTask = _eaData.GetEaPlaySubscriptionDetailsAsync();
        var ubiTask = _ubisoftData.GetUbisoftPlusSubscriptionDetailsAsync();

        await Task.WhenAll(xboxTask, eaTask, ubiTask);

        var xboxStatus = xboxTask.Result;
        var eaStatus = eaTask.Result;
        var ubiStatus = ubiTask.Result;

        XboxStatusText.Text = xboxStatus;
        EaStatusText.Text = eaStatus;
        UbiStatusText.Text = ubiStatus;

        bool hasGP = xboxStatus.Contains("Ultimate") || xboxStatus.Contains("PC");
        bool hasEA = eaStatus.Contains("EA Play");
        bool hasUbi = ubiStatus.Contains("Ubisoft+");

        // 2. Load Cached Wishlist and Filter
        Matches.Clear();
        if (File.Exists(_wishlistCachePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_wishlistCachePath);
                var cachedItems = JsonSerializer.Deserialize<List<SteamWishlistItemViewModel>>(json);
                
                if (cachedItems != null)
                {
                    var matches = cachedItems.Where(item => 
                        (item.IsOnGamePass && hasGP) ||
                        (item.IsOnEaPlay && hasEA) ||
                        (item.IsOnUbisoftPlus && hasUbi)
                    ).ToList();

                    foreach (var m in matches) Matches.Add(m);
                }
            }
            catch { }
        }

        LoadingRing.IsActive = false;
        NoMatchesText.Visibility = Matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    
    // Focus Handling for Controller
    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back)
        {
            await Task.Delay(100);
            // Focus first card if back
            // Or grid if items exist? Let's focus grid if possible.
            if (Matches.Count > 0)
                MatchesGridView.Focus(FocusState.Programmatic);
        }
    }
}
