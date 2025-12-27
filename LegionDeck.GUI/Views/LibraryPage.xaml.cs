using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LegionDeck.Core.Services;
using LegionDeck.GUI.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LegionDeck.GUI.Views;

public sealed partial class LibraryPage : Page
{
    private ObservableCollection<LibraryGameViewModel> InstalledGames { get; } = new();
    private List<LibraryGameViewModel> _allGames = new();
    private readonly LocalLibraryService _libraryService = new();
    private readonly SteamGridDbService _sgdbService;
    private readonly MetadataService _metadataService;
    private readonly GameEnrichmentService _enrichmentService;
    private readonly ConfigService _configService;
    private readonly SteamLibraryService _steamLibraryService;
    private readonly SubscriptionLibraryService _subLibraryService = new();
    private readonly LibraryCacheService _cacheService = new();
    private string _currentMode = "";
    private bool _isDataLoaded = false;
    private bool _isReturning = false;
    private readonly System.Threading.SemaphoreSlim _refreshSemaphore = new(1, 1);
    private System.Threading.CancellationTokenSource? _enrichmentCts;

    public LibraryPage()
    {
        this.InitializeComponent();
        this.AllowFocusOnInteraction = true;
        
        _configService = new ConfigService();
        _steamLibraryService = new SteamLibraryService(_configService);
        _sgdbService = new SteamGridDbService(_configService);
        _metadataService = new MetadataService();
        _enrichmentService = new GameEnrichmentService(_configService, _metadataService);

        LibraryGridView.ItemsSource = InstalledGames;
        this.Loaded += LibraryPage_Loaded;
    }

    private async void ViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isReturning) return; 

        string newMode = "Installed";
        if (ViewModeCombo != null && ViewModeCombo.SelectedItem is ComboBoxItem comboItem && comboItem.Tag != null)
        {
            newMode = comboItem.Tag.ToString() ?? "Installed";
        }

        if (newMode == _currentMode && _isDataLoaded) return;
        await RefreshLibraryAsync();
    }

    protected override void OnNavigatingFrom(Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs e)
    {
        _enrichmentCts?.Cancel();
        base.OnNavigatingFrom(e);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back) _isReturning = true;
        else _isReturning = false;
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log($"LibraryPage_Loaded (Returning: {_isReturning}, DataLoaded: {_isDataLoaded})");
            await Task.Delay(100);
            if (InstalledGames.Any())
            {
                if (LibraryGridView.SelectedIndex < 0) LibraryGridView.SelectedIndex = 0;
                var container = LibraryGridView.ContainerFromIndex(LibraryGridView.SelectedIndex) as Control;
                container?.Focus(FocusState.Programmatic);
            }
            if (!_isDataLoaded) await RefreshLibraryAsync();
            _isReturning = false; 
        }
        catch (Exception ex) { Log($"Error in LibraryPage_Loaded: {ex.Message}"); }
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LibraryPage] {message}\n");
        }
        catch {{ }}
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ForceRefreshLibraryAsync();

    private async Task ForceRefreshLibraryAsync()
    {
        // Reset the static flag to allow background update again
        LibraryUpdateService.ResetUpdateFlag();
        
        // Manually nuke the cache to force fresh data
        try
        {
            var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "Cache");
            if (Directory.Exists(cacheDir))
            {
                var files = Directory.GetFiles(cacheDir, "*.json");
                foreach (var file in files)
                {
                    // Don't delete wishlist_cache unless we want to, but for library refresh it's safer to clear EA/Steam/Xbox caches
                    if (!file.Contains("wishlist")) File.Delete(file);
                }
            }
        }
        catch { }

        await RefreshLibraryAsync();
    }

    private void SortByName_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item)
        {
            if (item.Tag?.ToString() == "NameAsc") _allGames = _allGames.OrderBy(g => g.Name).ToList();
            else _allGames = _allGames.OrderByDescending(g => g.Name).ToList();
            ApplyFilter(SearchBox.Text);
        }
    }

    private void SortBySource_Click(object sender, RoutedEventArgs e)
    {
        _allGames = _allGames.OrderBy(g => g.Source).ThenBy(g => g.Name).ToList();
        ApplyFilter(SearchBox.Text);
    }

    private async Task RefreshLibraryAsync()
    {
        _enrichmentCts?.Cancel();
        _enrichmentCts = new System.Threading.CancellationTokenSource();
        if (!await _refreshSemaphore.WaitAsync(0)) return; 

        try
        {
            Log($"RefreshLibraryAsync started for mode: {ViewModeCombo?.SelectedItem}");
            if (LoadingRing != null) LoadingRing.IsActive = true;
            if (NoGamesText != null) NoGamesText.Visibility = Visibility.Collapsed;
            
            _allGames.Clear();
            InstalledGames.Clear(); 
            
            string mode = "Installed";
            if (ViewModeCombo != null && ViewModeCombo.SelectedItem is ComboBoxItem comboItem && comboItem.Tag != null)
            {
                mode = comboItem.Tag.ToString() ?? "Installed";
            }
            _currentMode = mode;

            var localGames = await _libraryService.GetInstalledGamesAsync();

            if (mode == "Installed")
            {
                foreach (var game in localGames)
                {
                    var vm = new LibraryGameViewModel(game);
                    var cachedName = _metadataService.GetName(game.Id);
                    if (!string.IsNullOrEmpty(cachedName)) vm.Name = cachedName;
                    var cachedCover = _metadataService.GetCover(game.Id);
                    if (!string.IsNullOrEmpty(cachedCover)) vm.ImgCapsule = cachedCover;
                    _allGames.Add(vm);
                }
            }
            else
            {
                _allGames.Clear();
                var cachedList = await _cacheService.LoadLibraryAsync(mode);
                _libraryService.UpdateInstallationStatus(cachedList, localGames);
                
                foreach (var game in cachedList)
                {
                    var vm = new LibraryGameViewModel(game);
                    var cachedCover = _metadataService.GetCover(game.Id);
                    if (!string.IsNullOrEmpty(cachedCover)) vm.ImgCapsule = cachedCover;
                    _allGames.Add(vm);
                }

                if (_allGames.Count == 0)
                {
                    if (LoadingRing != null) LoadingRing.IsActive = true;
                    // Only scrape if cache is empty
                    var currentCts = _enrichmentCts; 
                    _ = Task.Run(async () => {
                        try {
                            var updater = new LibraryUpdateService();
                            await updater.UpdateAllAsync(); // This will respect the static _hasUpdated flag
                            
                            // Re-load from cache after update
                            var freshList = await _cacheService.LoadLibraryAsync(mode);
                            _libraryService.UpdateInstallationStatus(freshList, localGames);

                            this.DispatcherQueue.TryEnqueue(() => {
                                if (_currentMode == mode && !currentCts.IsCancellationRequested) {
                                    _allGames.Clear();
                                    Log($"Populating UI from fresh cache. Count: {freshList.Count}");
                                    foreach (var game in freshList) {
                                        if (game.Name.Contains("Veilguard", StringComparison.OrdinalIgnoreCase))
                                            Log($"[DEBUG] Adding Veilguard to UI List: {game.Name} (ID: {game.Id}, Img: {game.BackgroundImage})");
                                        
                                        var vm = new LibraryGameViewModel(game);
                                        var cachedCover = _metadataService.GetCover(game.Id);
                                        if (!string.IsNullOrEmpty(cachedCover)) vm.ImgCapsule = cachedCover;
                                        _allGames.Add(vm);
                                    }
                                    ApplyFilter(SearchBox?.Text ?? "");
                                    _ = EnrichLibraryAsync(currentCts.Token);
                                }
                            });
                        } catch (Exception ex) { Log($"On-demand refresh failed: {ex.Message}"); }
                    });
                }
            }
            
            ApplyFilter(SearchBox != null ? SearchBox.Text : "");
            if (NoGamesText != null) NoGamesText.Visibility = InstalledGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _isDataLoaded = true;
            _ = EnrichLibraryAsync(_enrichmentCts.Token);
        }
        catch (Exception ex) { Log($"Error in RefreshLibraryAsync: {ex.Message}"); }
        finally { if (LoadingRing != null) LoadingRing.IsActive = false; _refreshSemaphore.Release(); }
    }

    private async Task EnrichLibraryAsync(System.Threading.CancellationToken ct)
    {
        var gamesToScan = _allGames.ToList(); 
        var enrichQueue = new System.Collections.Generic.List<(string Id, string Name, string Source)>();
        foreach (var game in gamesToScan)
        {
            if (ct.IsCancellationRequested) return;
            
            // Only attempt Steam store enrichment for Steam games or Xbox games (which might be mapped)
            if (game.Source != "Steam" && game.Source != "Xbox") continue;

            var cachedCover = _metadataService.GetCover(game.GameData.Id);
            if (game.Type == "unknown" || game.Name.StartsWith("AppID ") || string.IsNullOrEmpty(cachedCover) || cachedCover.Contains("steamstatic.com"))
                enrichQueue.Add((game.GameData.Id, game.Name, game.Source));
        }

        try
        {
            Action<string, SteamStoreService.SteamStoreDetails> onGameUpdated = (idStr, details) =>
            {
                if (ct.IsCancellationRequested) return;
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    var gameVm = _allGames.FirstOrDefault(g => g.GameData.Id == idStr);
                    if (gameVm != null)
                    {
                        if (!string.IsNullOrEmpty(details.Name) && gameVm.Name != details.Name) gameVm.Name = details.Name;
                        if (!string.IsNullOrEmpty(details.Type) && gameVm.Type != details.Type)
                        {
                            gameVm.Type = details.Type;
                            if (gameVm.Source == "Steam" && !string.Equals(gameVm.Type, "game", StringComparison.OrdinalIgnoreCase))
                                if (InstalledGames.Contains(gameVm)) InstalledGames.Remove(gameVm);
                        }
                        string? imageToUse = !string.IsNullOrEmpty(details.VerticalCover) ? details.VerticalCover : null;
                        if (string.IsNullOrEmpty(imageToUse) && !string.IsNullOrEmpty(details.HeaderImage))
                        {
                             bool isLandscapeHeader = details.HeaderImage.Contains("header.jpg") && details.HeaderImage.Contains("steamstatic.com");
                             if (!isLandscapeHeader) imageToUse = details.HeaderImage;
                        }
                        if (!string.IsNullOrEmpty(imageToUse))
                        {
                             if (string.IsNullOrEmpty(gameVm.ImgCapsule) || gameVm.ImgCapsule.Contains("steamstatic.com") || details.VerticalCover != null) 
                             {
                                 gameVm.ImgCapsule = imageToUse;
                                 _metadataService.SetCover(gameVm.GameData.Id, imageToUse);
                             }
                        }
                    }
                });
            };
            await _enrichmentService.EnrichGamesBatchAsync(enrichQueue, onGameUpdated);
            this.DispatcherQueue.TryEnqueue(() => { if (!ct.IsCancellationRequested) ApplyFilter(SearchBox?.Text ?? ""); });
        }
        catch (Exception ex) { Log($"Batch enrichment error: {ex.Message}"); }
    }

    private void UpdateGameCover(LibraryGameViewModel game, string url)
    {
        _metadataService.SetCover(game.GameData.Id, url);
        this.DispatcherQueue.TryEnqueue(() => { game.ImgCapsule = url; });
    }

    private async void Image_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (e.ErrorMessage == "E_NETWORK_ERROR") return; 
        if (sender is Image img && img.DataContext is LibraryGameViewModel vm)
        {
            if (vm.Source == "Steam")
            {
                try { 
                    if (int.TryParse(vm.GameData.Id, out int steamAppId)) { 
                        var coverUrl = await _sgdbService.GetVerticalCoverAsync(steamAppId);
                        if (!string.IsNullOrEmpty(coverUrl)) { UpdateGameCover(vm, coverUrl); return; }
                    }
                } catch {{ }} 
            }
            try {
                var gameId = await _sgdbService.SearchGameIdAsync(vm.Name);
                if (gameId.HasValue) {
                    var coverUrl = await _sgdbService.GetVerticalCoverByGameIdAsync(gameId.Value);
                    if (!string.IsNullOrEmpty(coverUrl)) UpdateGameCover(vm, coverUrl);
                }
            } catch {{ }}
        }
    }

    private void HideGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is LibraryGameViewModel vm)
        {
            _metadataService.SetHidden(vm.GameData.Id, true);
            InstalledGames.Remove(vm);
            _allGames.Remove(vm);
        }
    }

    private void ApplyFilter(string filter)
    {
        InstalledGames.Clear();
        var filtered = string.IsNullOrWhiteSpace(filter) ? _allGames : _allGames.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        Log($"ApplyFilter: filter='{filter}', input_count={_allGames.Count}, filtered_count={filtered.Count()}");
        foreach (var game in filtered)
        {
            if (game.Name.Contains("Veilguard", StringComparison.OrdinalIgnoreCase))
                Log($"[DEBUG] ApplyFilter processing Veilguard: Hidden={_metadataService.IsHidden(game.GameData.Id)}");

            if (_metadataService.IsHidden(game.GameData.Id)) continue;
            if (game.Source == "Steam")
            {
                if (!string.Equals(game.Type, "game", StringComparison.OrdinalIgnoreCase) && !string.Equals(game.Type, "unknown", StringComparison.OrdinalIgnoreCase)) continue;
            }
            InstalledGames.Add(game);
        }
        if (NoGamesText != null) NoGamesText.Visibility = InstalledGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ApplyFilter(sender.Text); }
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) { ApplyFilter(sender.Text); }
    private async void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is LibraryGameViewModel vm) this.Frame.Navigate(typeof(GameDetailsPage), vm); }
    private void LibraryGridView_GettingFocus(UIElement sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        if (args.NewFocusedElement == LibraryGridView && LibraryGridView.Items.Count > 0)
        {
            if (LibraryGridView.SelectedIndex < 0) LibraryGridView.SelectedIndex = 0;
            var container = LibraryGridView.ContainerFromIndex(LibraryGridView.SelectedIndex) as Control;
            if (container != null) { args.TrySetNewFocusedElement(container); args.Handled = true; }
        }
    }

    private async void LibraryGridView_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.GamepadX || e.Key == Windows.System.VirtualKey.X)
        {
            if (LibraryGridView.SelectedItem is LibraryGameViewModel vm) await _libraryService.LaunchGameAsync(vm.GameData);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.GamepadY || e.Key == Windows.System.VirtualKey.Y)
        {
            SearchBox.Focus(FocusState.Programmatic);
            e.Handled = true; 
        }
    }
}
