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
    private readonly LibraryCacheService _cacheService = new();
    private string _currentMode = "";
    private bool _isDataLoaded = false;
    private bool _isReturning = false;
    private bool _isSyncing = false;
    private readonly System.Threading.SemaphoreSlim _refreshSemaphore = new(1, 1);
    private System.Threading.CancellationTokenSource? _enrichmentCts;

    public LibraryPage()
    {
        this.InitializeComponent();
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
        string newMode = (ViewModeCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Installed";
        if (newMode == _currentMode && _isDataLoaded) return;
        await RefreshLibraryAsync(forceUpdate: false);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isReturning = (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back);
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        Log($"LibraryPage_Loaded (Returning: {_isReturning})");
        if (!_isDataLoaded) await RefreshLibraryAsync(forceUpdate: false);
        _isReturning = false;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ForceRefreshLibraryAsync();

    private async Task ForceRefreshLibraryAsync()
    {
        if (_isSyncing) return;
        Log("Force Refresh Requested.");
        LibraryUpdateService.ResetUpdateFlag();
        await RefreshLibraryAsync(forceUpdate: true);
    }

    private async Task RefreshLibraryAsync(bool forceUpdate = false)
    {
        if (!await _refreshSemaphore.WaitAsync(0)) return;
        try
        {
            _enrichmentCts?.Cancel();
            _enrichmentCts = new System.Threading.CancellationTokenSource();
            
            string mode = (ViewModeCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Installed";
            _currentMode = mode;
            Log($"Refresh: Mode={mode}, Force={forceUpdate}");

            if (LoadingRing != null) LoadingRing.IsActive = true;

            var localGames = await _libraryService.GetInstalledGamesAsync();
            var list = (mode == "Installed") ? localGames : await _cacheService.LoadLibraryAsync(mode);
            _libraryService.UpdateInstallationStatus(list, localGames);

            _allGames = list.Select(g => {
                var vm = new LibraryGameViewModel(g);
                var cov = _metadataService.GetCover(g.Id);
                if (!string.IsNullOrEmpty(cov)) vm.ImgCapsule = cov;
                return vm;
            }).ToList();

            ApplyFilter(SearchBox?.Text ?? "");

            // Trigger Enrichment for current view
            var enrichmentList = _allGames.Select(g => (g.GameData.Id, g.Name, g.Source)).ToList();
            _ = _enrichmentService.EnrichGamesBatchAsync(enrichmentList, (id, details) => {
                this.DispatcherQueue.TryEnqueue(() => {
                    var vm = _allGames.FirstOrDefault(g => g.GameData.Id == id);
                    if (vm != null) {
                        if (!string.IsNullOrEmpty(details.VerticalCover)) vm.ImgCapsule = details.VerticalCover;
                        if (!string.IsNullOrEmpty(details.Name)) vm.Name = details.Name;
                    }
                });
            });

            if ((_allGames.Count == 0 || forceUpdate) && !_isSyncing)
            {
                _isSyncing = true;
                _ = Task.Run(async () => {
                    try {
                        var updater = new LibraryUpdateService();
                        await updater.UpdateAllAsync(mode);
                        var fresh = await _cacheService.LoadLibraryAsync(mode);
                        _libraryService.UpdateInstallationStatus(fresh, localGames);
                        this.DispatcherQueue.TryEnqueue(() => {
                            if (_currentMode == mode) {
                                _allGames = fresh.Select(g => {
                                    var vm = new LibraryGameViewModel(g);
                                    var cov = _metadataService.GetCover(g.Id);
                                    if (!string.IsNullOrEmpty(cov)) vm.ImgCapsule = cov;
                                    return vm;
                                }).ToList();
                                ApplyFilter(SearchBox?.Text ?? "");
                                
                                // Re-trigger Enrichment for fresh list
                                var freshEnrichList = _allGames.Select(g => (g.GameData.Id, g.Name, g.Source)).ToList();
                                _ = _enrichmentService.EnrichGamesBatchAsync(freshEnrichList, (id, details) => {
                                    this.DispatcherQueue.TryEnqueue(() => {
                                        var vm = _allGames.FirstOrDefault(g => g.GameData.Id == id);
                                        if (vm != null) {
                                            if (!string.IsNullOrEmpty(details.VerticalCover)) vm.ImgCapsule = details.VerticalCover;
                                            if (!string.IsNullOrEmpty(details.Name)) vm.Name = details.Name;
                                        }
                                    });
                                });
                            }
                        });
                    } finally { _isSyncing = false; this.DispatcherQueue.TryEnqueue(() => { if (LoadingRing != null) LoadingRing.IsActive = false; }); }
                });
            }
            _isDataLoaded = true;
        } finally { _refreshSemaphore.Release(); if (!_isSyncing && LoadingRing != null) LoadingRing.IsActive = false; }
    }

    private void SortByName_Click(object sender, RoutedEventArgs e) {
        bool asc = (sender as MenuFlyoutItem)?.Tag?.ToString() == "NameAsc";
        _allGames = asc ? _allGames.OrderBy(g => g.Name).ToList() : _allGames.OrderByDescending(g => g.Name).ToList();
        ApplyFilter(SearchBox?.Text ?? "");
    }

    private void SortBySource_Click(object sender, RoutedEventArgs e) {
        _allGames = _allGames.OrderBy(g => g.Source).ThenBy(g => g.Name).ToList();
        ApplyFilter(SearchBox?.Text ?? "");
    }

    private void ApplyFilter(string filter)
    {
        InstalledGames.Clear();
        var filtered = string.IsNullOrWhiteSpace(filter) ? _allGames : _allGames.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (var vm in filtered) {
            if (!_metadataService.IsHidden(vm.GameData.Id)) InstalledGames.Add(vm);
        }
        if (NoGamesText != null) NoGamesText.Visibility = InstalledGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ApplyFilter(sender.Text); }
    private async void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is LibraryGameViewModel vm) this.Frame.Navigate(typeof(GameDetailsPage), vm); }
    
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) { ApplyFilter(sender.Text); }
    
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
                } catch { } 
            }
            try {
                var gameId = await _sgdbService.SearchGameIdAsync(vm.Name);
                if (gameId.HasValue) {
                    var coverUrl = await _sgdbService.GetVerticalCoverByGameIdAsync(gameId.Value);
                    if (!string.IsNullOrEmpty(coverUrl)) UpdateGameCover(vm, coverUrl);
                }
            } catch { }
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

    private void UpdateGameCover(LibraryGameViewModel game, string url)
    {
        _metadataService.SetCover(game.GameData.Id, url);
        this.DispatcherQueue.TryEnqueue(() => { game.ImgCapsule = url; });
    }

    private void Log(string message) {
        try {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LibraryPage] {message}\n");
        } catch { }
    }
}
