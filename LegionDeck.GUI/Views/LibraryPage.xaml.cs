using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LegionDeck.Core.Services;
using LegionDeck.GUI.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System;

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

        // Prevent reload if mode hasn't changed AND we have data
        if (newMode == _currentMode && _isDataLoaded) return;

        await RefreshLibraryAsync();
    }

    protected override void OnNavigatingFrom(Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs e)
    {
        // Cancel background work when leaving page
        _enrichmentCts?.Cancel();
        base.OnNavigatingFrom(e);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back)
        {
            _isReturning = true;
        }
        else
        {
            _isReturning = false;
        }
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log($"LibraryPage_Loaded (Returning: {_isReturning}, DataLoaded: {_isDataLoaded})");
            
            // Focus logic
            await Task.Delay(100);
            if (InstalledGames.Any())
            {
                if (LibraryGridView.SelectedIndex < 0) LibraryGridView.SelectedIndex = 0;
                var container = LibraryGridView.ContainerFromIndex(LibraryGridView.SelectedIndex) as Control;
                container?.Focus(FocusState.Programmatic);
            }

            // Only refresh if we haven't loaded anything yet
            if (!_isDataLoaded)
            {
                await RefreshLibraryAsync();
            }
            
            _isReturning = false; 
        }
        catch (Exception ex)
        {
            Log($"Error in LibraryPage_Loaded: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            System.IO.File.AppendAllText(path, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LibraryPage] {message}\n");
        }
        catch { }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshLibraryAsync();

    private void SortByName_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item)
        {
            if (item.Tag?.ToString() == "NameAsc")
                _allGames = _allGames.OrderBy(g => g.Name).ToList();
            else
                _allGames = _allGames.OrderByDescending(g => g.Name).ToList();
            
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
        // Cancel any existing enrichment
        _enrichmentCts?.Cancel();
        _enrichmentCts = new System.Threading.CancellationTokenSource();

        if (!await _refreshSemaphore.WaitAsync(0)) return; // Prevent concurrent refreshes

        try
        {
            Log("RefreshLibraryAsync started");
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

            if (mode == "Installed")
            {
                var games = await _libraryService.GetInstalledGamesAsync();
                foreach (var game in games)
                {
                    var vm = new LibraryGameViewModel(game);
                    var cachedName = _metadataService.GetName(game.Id);
                    if (!string.IsNullOrEmpty(cachedName)) vm.Name = cachedName;
                    var cachedCover = _metadataService.GetCover(game.Id);
                    if (!string.IsNullOrEmpty(cachedCover)) vm.ImgCapsule = cachedCover;
                    _allGames.Add(vm);
                }
            }
            else if (mode == "Steam")
            {
                var items = await _steamLibraryService.GetOwnedGamesAsync();
                foreach (var item in items)
                {
                    var gameData = new LocalLibraryService.InstalledGame { Id = item.AppId.ToString(), Name = item.Name, Source = "Steam", IsInstalled = false };
                    var vm = new LibraryGameViewModel(gameData);
                    var cachedType = _metadataService.GetType(gameData.Id);
                    vm.Type = !string.IsNullOrEmpty(cachedType) ? cachedType : "unknown";
                    var cachedName = _metadataService.GetName(gameData.Id);
                    if (!string.IsNullOrEmpty(cachedName)) vm.Name = cachedName;
                    
                    vm.ImgCapsule = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{item.AppId}/library_600x900_2x.jpg";
                    var cachedCover = _metadataService.GetCover(item.AppId.ToString());
                    if (!string.IsNullOrEmpty(cachedCover)) vm.ImgCapsule = cachedCover;

                    _allGames.Add(vm);
                }
            }
            
            ApplyFilter(SearchBox != null ? SearchBox.Text : "");
            if (NoGamesText != null) NoGamesText.Visibility = InstalledGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            
            _isDataLoaded = true;
            Log("RefreshLibraryAsync completed. Starting enrichment...");
            _ = EnrichLibraryAsync(_enrichmentCts.Token);
        }
        catch (Exception ex)
        {
            Log($"Error in RefreshLibraryAsync: {ex.Message}");
        }
        finally
        {
            if (LoadingRing != null) LoadingRing.IsActive = false;
            _refreshSemaphore.Release();
        }
    }

    private async Task EnrichLibraryAsync(System.Threading.CancellationToken ct)
    {
        var gamesToScan = _allGames.ToList(); // Copy
        var enrichQueue = new System.Collections.Generic.List<(string Id, string Name, string Source)>();

        foreach (var game in gamesToScan)
        {
            if (ct.IsCancellationRequested) return;

            if (game.Source == "Steam")
            {
                var cachedCover = _metadataService.GetCover(game.GameData.Id);
                if (game.Type == "unknown" || 
                    game.Name.StartsWith("AppID ") || 
                    string.IsNullOrEmpty(cachedCover) || 
                    cachedCover.Contains("steamstatic.com") || 
                    cachedCover.Contains("cloudflare.steamstatic.com"))
                {
                    enrichQueue.Add((game.GameData.Id, game.Name, game.Source));
                }
            }
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
                        if (!string.IsNullOrEmpty(details.Name) && gameVm.Name != details.Name)
                        {
                            gameVm.Name = details.Name;
                        }

                        if (!string.IsNullOrEmpty(details.Type) && gameVm.Type != details.Type)
                        {
                            gameVm.Type = details.Type;
                            if (gameVm.Source == "Steam" && !string.Equals(gameVm.Type, "game", StringComparison.OrdinalIgnoreCase))
                            {
                                if (InstalledGames.Contains(gameVm)) InstalledGames.Remove(gameVm);
                            }
                        }
                        
                        string? imageToUse = !string.IsNullOrEmpty(details.VerticalCover) ? details.VerticalCover : null;
                        if (string.IsNullOrEmpty(imageToUse) && !string.IsNullOrEmpty(details.HeaderImage))
                        {
                             bool isLandscapeHeader = details.HeaderImage.Contains("header.jpg") && details.HeaderImage.Contains("steamstatic.com");
                             if (!isLandscapeHeader) imageToUse = details.HeaderImage;
                        }

                        if (!string.IsNullOrEmpty(imageToUse))
                        {
                             if (string.IsNullOrEmpty(gameVm.ImgCapsule) || 
                                 gameVm.ImgCapsule.Contains("cloudflare.steamstatic.com") || 
                                 gameVm.ImgCapsule.Contains("library_600x900") ||
                                 details.VerticalCover != null) 
                             {
                                 gameVm.ImgCapsule = imageToUse;
                                 _metadataService.SetCover(idStr, imageToUse);
                             }
                        }
                    }
                });
            };

            await _enrichmentService.EnrichGamesBatchAsync(enrichQueue, onGameUpdated);
            
            this.DispatcherQueue.TryEnqueue(() => 
            {
                if (!ct.IsCancellationRequested)
                    ApplyFilter(SearchBox != null ? SearchBox.Text : "");
            });
        }
        catch (Exception ex)
        {
            Log($"Batch enrichment error: {ex.Message}");
        }
    }

    private void RemoveGame(LibraryGameViewModel game)
    {
        // No longer needed, filtering handles it
    }

    private void UpdateGameName(LibraryGameViewModel game, string name)
    {
        this.DispatcherQueue.TryEnqueue(() => 
        {
            game.Name = name;
        });
    }

    private async Task<bool> IsUrlValidAsync(System.Net.Http.HttpClient client, string url)
    {
        try
        {
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
            var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateGameCover(LibraryGameViewModel game, string url)
    {
        _metadataService.SetCover(game.GameData.Id, url);
        this.DispatcherQueue.TryEnqueue(() => 
        {
            game.ImgCapsule = url;
        });
    }

    private async void Image_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (e.ErrorMessage == "E_NETWORK_ERROR") return; 
        
        if (sender is Image img && img.DataContext is LibraryGameViewModel vm)
        {
            Log($"Image failed for {vm.Name} ({vm.Source}). Attempting SGDB fallback.");
            
            if (vm.Source == "Steam")
            {
                try
                {
                    if (int.TryParse(vm.GameData.Id, out int steamAppId))
                    {
                        var coverUrl = await _sgdbService.GetVerticalCoverAsync(steamAppId);
                        if (!string.IsNullOrEmpty(coverUrl))
                        {
                            UpdateGameCover(vm, coverUrl);
                            return;
                        }
                    }
                }
                catch { }
            }
            
            try
            {
                var gameId = await _sgdbService.SearchGameIdAsync(vm.Name);
                if (gameId.HasValue)
                {
                    var coverUrl = await _sgdbService.GetVerticalCoverByGameIdAsync(gameId.Value);
                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        UpdateGameCover(vm, coverUrl);
                    }
                }
            }
            catch { }
        }
    }

    private void HideGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is LibraryGameViewModel vm)
        {
            Log($"Hiding game: {vm.Name}");
            _metadataService.SetHidden(vm.GameData.Id, true);
            
            // Remove from visible collection directly to maintain scroll position
            InstalledGames.Remove(vm);
            
            // Also remove from master list so it doesn't reappear on filter changes
            _allGames.Remove(vm);
        }
    }

    private void ApplyFilter(string filter)
    {
        InstalledGames.Clear();
        var filtered = string.IsNullOrWhiteSpace(filter) 
            ? _allGames 
            : _allGames.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var game in filtered)
        {
            // Persistent Hidden Check
            if (_metadataService.IsHidden(game.GameData.Id))
            {
                continue;
            }

            // Strict filtering: If Steam, must be 'game' or 'unknown'. 
            // We hide 'dlc', 'music', 'tool', etc.
            if (game.Source == "Steam")
            {
                if (!string.Equals(game.Type, "game", StringComparison.OrdinalIgnoreCase) && 
                    !string.Equals(game.Type, "unknown", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            InstalledGames.Add(game);
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ApplyFilter(sender.Text);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ApplyFilter(sender.Text);
    }

    private async void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LibraryGameViewModel vm)
        {
            Log($"Game clicked: {vm.Name}. Navigating to details.");
            this.Frame.Navigate(typeof(GameDetailsPage), vm);
        }
    }

    private void LibraryGridView_GettingFocus(UIElement sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        // If the focus is moving TO the GridView itself (not an item inside it)
        // We redirect it to the selected item or first item.
        if (args.NewFocusedElement == LibraryGridView && LibraryGridView.Items.Count > 0)
        {
            if (LibraryGridView.SelectedIndex < 0) LibraryGridView.SelectedIndex = 0;
            
            var container = LibraryGridView.ContainerFromIndex(LibraryGridView.SelectedIndex) as Control;
            if (container != null)
            {
                args.TrySetNewFocusedElement(container);
                args.Handled = true;
            }
        }
    }

    private async void LibraryGridView_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.GamepadX || e.Key == Windows.System.VirtualKey.X)
        {
            if (LibraryGridView.SelectedItem is LibraryGameViewModel vm)
            {
                Log($"Gamepad X (or Key X) pressed for {vm.Name}. Launching...");
                await _libraryService.LaunchGameAsync(vm.GameData);
                e.Handled = true;
            }
        }
        else if (e.Key == Windows.System.VirtualKey.GamepadY || e.Key == Windows.System.VirtualKey.Y)
        {
            SearchBox.Focus(FocusState.Programmatic);
            // Don't handle it, let it bubble or just focus. 
            // Actually, focusing programmatically is enough.
            e.Handled = true; 
        }
    }
}