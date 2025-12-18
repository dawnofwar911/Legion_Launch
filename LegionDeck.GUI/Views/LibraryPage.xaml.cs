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

    public LibraryPage()
    {
        this.InitializeComponent();
        this.AllowFocusOnInteraction = true;
        
        var configService = new ConfigService();
        _sgdbService = new SteamGridDbService(configService);
        _metadataService = new MetadataService();

        LibraryGridView.ItemsSource = InstalledGames;
        this.Loaded += LibraryPage_Loaded;
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log("LibraryPage_Loaded started");
            // Only refresh if we haven't loaded games yet (NavigationCacheMode handles the rest)
            if (_allGames.Count == 0) await RefreshLibraryAsync();
            
            await Task.Delay(200);
            Log("Setting focus to LibraryGridView");
            LibraryGridView.Focus(FocusState.Programmatic);
            // Only select if not already selected
            if (InstalledGames.Any() && LibraryGridView.SelectedIndex < 0) LibraryGridView.SelectedIndex = 0;
            Log("LibraryPage_Loaded completed");
        }
        catch (Exception ex)
        {
            Log($"Error in LibraryPage_Loaded: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ... existing Log method ...
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

    private async Task RefreshLibraryAsync()
    {
        try
        {
            Log("RefreshLibraryAsync started");
            _allGames.Clear();
            var games = await _libraryService.GetInstalledGamesAsync();
            Log($"Found {games.Count} games");
            
            foreach (var game in games)
            {
                var vm = new LibraryGameViewModel(game);
                
                // Check local cache for custom cover
                var cachedCover = _metadataService.GetCover(game.Id);
                if (!string.IsNullOrEmpty(cachedCover))
                {
                    vm.ImgCapsule = cachedCover;
                }
                
                _allGames.Add(vm);
            }
            
            ApplyFilter(SearchBox.Text);
            
            // Start background artwork fetch for non-Steam games (or missing cache)
            _ = LoadMissingArtworkAsync();
            
            Log("RefreshLibraryAsync completed");
        }
        catch (Exception ex)
        {
            Log($"Error in RefreshLibraryAsync: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async Task LoadMissingArtworkAsync()
    {
        // Scan all games that don't have a manually cached cover yet
        var gamesToScan = _allGames.Where(g => !_metadataService.HasCover(g.GameData.Id)).ToList();
        using var httpClient = new System.Net.Http.HttpClient();

        foreach (var game in gamesToScan)
        {
            try
            {
                if (game.Source == "Steam")
                {
                    // For Steam games, first check if the default URL is valid (returns 200 OK)
                    // The ViewModel sets ImgCapsule to the default Steam URL by default
                    var defaultUrl = game.ImgCapsule;
                    bool isValid = await IsUrlValidAsync(httpClient, defaultUrl);

                    if (isValid)
                    {
                        // It's valid, so we cache it as the "confirmed" cover so we don't check again
                        _metadataService.SetCover(game.GameData.Id, defaultUrl);
                        continue; 
                    }
                    else
                    {
                        Log($"Default Steam artwork failed for {game.Name} (ID: {game.GameData.Id}). Searching SGDB...");
                    }
                }

                // If not Steam, or if Steam URL was invalid:
                var gameId = await _sgdbService.SearchGameIdAsync(game.Name);
                if (gameId.HasValue)
                {
                    var coverUrl = await _sgdbService.GetVerticalCoverByGameIdAsync(gameId.Value);
                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        UpdateGameCover(game, coverUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error fetching artwork for {game.Name}: {ex.Message}");
            }
        }
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
        if (e.ErrorMessage == "E_NETWORK_ERROR") return; // Ignore transient network issues?
        
        // Find the ViewModel associated with this image
        if (sender is Image img && img.DataContext is LibraryGameViewModel vm)
        {
            Log($"Image failed for {vm.Name} ({vm.Source}). Attempting SGDB fallback.");
            
            // If it's a Steam game failing, try to fetch via SGDB
            if (vm.Source == "Steam")
            {
                try
                {
                    // For Steam games, we can search by AppID directly which is more accurate
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
            
            // Fallback: Search by name if AppID lookup failed or not Steam
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

    private void ApplyFilter(string filter)
    {
        InstalledGames.Clear();
        var filtered = string.IsNullOrWhiteSpace(filter) 
            ? _allGames 
            : _allGames.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var game in filtered)
        {
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
            Log($"Game clicked: {vm.Name} (Source: {vm.Source}, ID: {vm.GameData.Id})");
            await _libraryService.LaunchGameAsync(vm.GameData);
        }
    }
}