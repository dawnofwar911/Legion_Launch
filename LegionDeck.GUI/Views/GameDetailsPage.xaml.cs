using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using LegionDeck.GUI.Models;
using LegionDeck.Core.Services;
using System;
using System.Diagnostics;
using Windows.UI;
using System.Threading.Tasks;

namespace LegionDeck.GUI.Views;

public sealed partial class GameDetailsPage : Page
{
    private object? _gameViewModel;
    private readonly LocalLibraryService _libraryService = new();
    private readonly SteamGridDbService _sgdbService;
    private readonly ConfigService _configService;
    private readonly MetadataService _metadataService;

    public GameDetailsPage()
    {
        this.InitializeComponent();
        _configService = new ConfigService();
        _sgdbService = new SteamGridDbService(_configService);
        _metadataService = new MetadataService();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _gameViewModel = e.Parameter;

        if (_gameViewModel is LibraryGameViewModel libraryVM)
        {
            GameTitle.Text = libraryVM.Name;
            GameSource.Text = libraryVM.Source;
            SetBadgeColor(libraryVM.Source);
            
            // Check cache for hero image first
            var cachedHero = _metadataService.GetHero(libraryVM.GameData.Id);
            if (!string.IsNullOrEmpty(cachedHero))
            {
                HeroImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(cachedHero));
            }
            else
            {
                // Fallback to capsule while loading, or just empty if you prefer
                HeroImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(libraryVM.ImgCapsule));
                _ = LoadHeroImageAsync(libraryVM, libraryVM.GameData.Id);
            }

            PlayButton.Visibility = Visibility.Visible;
            GameDescription.Text = $"This game is installed from {libraryVM.Source}.";
        }
        else if (_gameViewModel is SteamWishlistItemViewModel wishlistVM)
        {
            GameTitle.Text = wishlistVM.Name;
            GameSource.Text = "Steam";
            SetBadgeColor("Steam");
            
            var cachedHero = _metadataService.GetHero(wishlistVM.AppId.ToString());
            if (!string.IsNullOrEmpty(cachedHero))
            {
                HeroImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(cachedHero));
            }
            else
            {
                HeroImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(wishlistVM.ImgCapsule));
                _ = LoadHeroImageAsync(wishlistVM, wishlistVM.AppId.ToString());
            }

            StoreButton.Visibility = Visibility.Visible;
            
            SubscriptionPanel.Visibility = (wishlistVM.IsOnGamePass || wishlistVM.IsOnEaPlay || wishlistVM.IsOnUbisoftPlus) 
                ? Visibility.Visible : Visibility.Collapsed;
            
            GPBadge.Visibility = wishlistVM.GamePassVisibility;
            EABadge.Visibility = wishlistVM.EaPlayVisibility;
            UbiBadge.Visibility = wishlistVM.UbisoftPlusVisibility;

            GameDescription.Text = "This game is on your Steam Wishlist.";
        }
    }

    private async Task LoadHeroImageAsync(object viewModel, string cacheId)
    {
        string? heroUrl = null;

        if (viewModel is SteamWishlistItemViewModel wishlistVM)
        {
            heroUrl = await _sgdbService.GetHeroImageAsync(wishlistVM.AppId);
        }
        else if (viewModel is LibraryGameViewModel libraryVM)
        {
            if (libraryVM.Source == "Steam" && int.TryParse(libraryVM.GameData.Id, out int appId))
            {
                heroUrl = await _sgdbService.GetHeroImageAsync(appId);
            }
            
            if (string.IsNullOrEmpty(heroUrl))
            {
                var gameId = await _sgdbService.SearchGameIdAsync(libraryVM.Name);
                if (gameId.HasValue)
                {
                    heroUrl = await _sgdbService.GetHeroImageByGameIdAsync(gameId.Value);
                }
            }
        }

        if (!string.IsNullOrEmpty(heroUrl))
        {
            _metadataService.SetHero(cacheId, heroUrl);
            DispatcherQueue.TryEnqueue(() =>
            {
                HeroImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(heroUrl));
            });
        }
    }

    private void SetBadgeColor(string source)
    {
        Color color = source.ToLower() switch
        {
            "steam" => Color.FromArgb(255, 23, 26, 33), // Steam Dark Blue/Grey
            "xbox" => Color.FromArgb(255, 16, 124, 16), // Xbox Green
            "ea" => Color.FromArgb(255, 255, 71, 71),   // EA Red
            "ubisoft" => Color.FromArgb(255, 0, 112, 255), // Ubisoft Blue
            "epic" => Color.FromArgb(255, 51, 51, 51),    // Epic Grey
            _ => (Color)Resources["SystemAccentColor"]
        };
        SourceBadge.Background = new SolidColorBrush(color);
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gameViewModel is LibraryGameViewModel libraryVM)
        {
            await _libraryService.LaunchGameAsync(libraryVM.GameData);
        }
    }

    private void StoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gameViewModel is SteamWishlistItemViewModel wishlistVM)
        {
            var uri = $"steam://store/{wishlistVM.AppId}";
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack)
        {
            this.Frame.GoBack();
        }
    }
}
