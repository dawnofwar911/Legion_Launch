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
using Microsoft.UI.Xaml.Input;

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
    private bool _isDataLoaded = false;
    private bool _isReturning = false;
    private bool _isSyncing = false;
    private readonly System.Threading.SemaphoreSlim _refreshSemaphore = new(1, 1);
    private System.Threading.CancellationTokenSource? _enrichmentCts;
    private int _lastSelectedIndex = -1;

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

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isReturning = (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back);

        if (_isReturning)
        {
            // Restore focus to grid when coming back
            await Task.Delay(100);
            
            if (_lastSelectedIndex >= 0 && _lastSelectedIndex < LibraryGridView.Items.Count)
            {
                var item = LibraryGridView.Items[_lastSelectedIndex];
                LibraryGridView.ScrollIntoView(item);
                
                var container = LibraryGridView.ContainerFromIndex(_lastSelectedIndex) as Control;
                container?.Focus(FocusState.Keyboard);
            }
            else
            {
                FocusGameGrid();
            }
        }
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        Log($"LibraryPage_Loaded (Returning: {_isReturning})");
        if (!_isDataLoaded) 
        {
            await RefreshLibraryAsync(forceUpdate: false);
        }
        else if (!_isReturning)
        {
            FocusGameGrid();
        }
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

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        ApplyAdvancedFilters();
    }

    private async Task RefreshLibraryAsync(bool forceUpdate = false)
    {
        if (!await _refreshSemaphore.WaitAsync(0)) return;
        try
        {
            _enrichmentCts?.Cancel();
            _enrichmentCts = new System.Threading.CancellationTokenSource();
            
            if (LoadingRing != null) LoadingRing.IsActive = true;

            // 1. Get Local (Installed)
            var localGames = await _libraryService.GetInstalledGamesAsync();
            
            // 2. Load Caches (All Sources) if not forcing update, otherwise we sync first
            if (forceUpdate)
            {
                _isSyncing = true;
                await Task.Run(async () => {
                    var updater = new LibraryUpdateService();
                    await updater.UpdateAllAsync(); // Syncs all clouds
                });
                _isSyncing = false;

                // Check for errors to display in InfoBar
                this.DispatcherQueue.TryEnqueue(() => {
                    if (LibraryUpdateService.LastErrors.TryGetValue("Battle.net", out var bnetError))
                    {
                        SyncInfoBar.Message = bnetError;
                        SyncInfoBar.Severity = InfoBarSeverity.Warning;
                        SyncInfoBar.Title = "Battle.net Sync Warning";
                        SyncInfoBar.IsOpen = true;
                    }
                    else
                    {
                        SyncInfoBar.IsOpen = false;
                    }
                });
            }

            var steamGames = await _cacheService.LoadLibraryAsync("Steam");
            var xboxGames = await _cacheService.LoadLibraryAsync("Xbox");
            var ubiGames = await _cacheService.LoadLibraryAsync("Ubisoft");
            var eaGames = await _cacheService.LoadLibraryAsync("EA");
            var epicGames = await _cacheService.LoadLibraryAsync("Epic");
            var bnetGames = await _cacheService.LoadLibraryAsync("Battle.net");

            // 3. Merge
            var merged = new Dictionary<string, LocalLibraryService.InstalledGame>();

            void MergeList(List<LocalLibraryService.InstalledGame> list)
            {
                foreach (var g in list)
                {
                    var key = $"{g.Source}_{g.Id}";
                    if (g.Source == "Xbox")
                    {
                        var existingXbox = merged.Values.FirstOrDefault(x => x.Source == "Xbox" && x.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
                        if (existingXbox != null) key = $"{existingXbox.Source}_{existingXbox.Id}";
                    }

                    if (!merged.ContainsKey(key))
                    {
                        merged[key] = g;
                    }
                    else if (g.IsInstalled) 
                    {
                        merged[key] = g; 
                    }
                }
            }

            _libraryService.UpdateInstallationStatus(steamGames, localGames);
            _libraryService.UpdateInstallationStatus(xboxGames, localGames);
            _libraryService.UpdateInstallationStatus(ubiGames, localGames);
            _libraryService.UpdateInstallationStatus(eaGames, localGames);
            _libraryService.UpdateInstallationStatus(epicGames, localGames);
            _libraryService.UpdateInstallationStatus(bnetGames, localGames);

            MergeList(localGames);
            MergeList(steamGames);
            MergeList(xboxGames);
            MergeList(ubiGames);
            MergeList(eaGames);
            MergeList(epicGames);
            MergeList(bnetGames);

            _allGames = merged.Values.Select(g => {
                var vm = new LibraryGameViewModel(g);
                var cov = _metadataService.GetCover(g.Id);
                if (!string.IsNullOrEmpty(cov)) vm.ImgCapsule = cov;
                return vm;
            }).ToList();

            ApplyAdvancedFilters();

            // Enrich visible games
            _ = _enrichmentService.EnrichGamesBatchAsync(_allGames.Select(g => (g.GameData.Id, g.Name, g.Source)).ToList(), (id, details) => {
                this.DispatcherQueue.TryEnqueue(() => {
                    var vm = _allGames.FirstOrDefault(g => g.GameData.Id == id);
                    if (vm != null) {
                        if (!string.IsNullOrEmpty(details.VerticalCover))
                        {
                            vm.ImgCapsule = details.VerticalCover + $"?t={DateTimeOffset.Now.ToUnixTimeSeconds()}";
                        }
                        if (!string.IsNullOrEmpty(details.Name)) vm.Name = details.Name;
                    }
                });
            }, _enrichmentCts.Token);

            _isDataLoaded = true;
            if (!_isReturning) FocusGameGrid();
        } 
        finally 
        { 
            _refreshSemaphore.Release(); 
            if (!_isSyncing && LoadingRing != null) LoadingRing.IsActive = false; 
        } 
    }

    private void ApplyAdvancedFilters()
    {
        if (_allGames == null) return;

        bool showInstalled = IsFilterChecked("Filter_Installed");
        bool showCloud = IsFilterChecked("Filter_Cloud");
        
        bool sourceSteam = IsFilterChecked("Source_Steam");
        bool sourceXbox = IsFilterChecked("Source_Xbox");
        bool sourceUbi = IsFilterChecked("Source_Ubisoft");
        bool sourceEA = IsFilterChecked("Source_EA");
        bool sourceEpic = IsFilterChecked("Source_Epic");
        bool sourceBNet = IsFilterChecked("Source_BattleNet");

        bool stateClaimed = IsFilterChecked("State_Claimed");
        bool stateUnclaimed = IsFilterChecked("State_Unclaimed");

        var filtered = _allGames.Where(g =>
        {
            bool statusMatch = (showInstalled && g.IsInstalled) || (showCloud && !g.IsInstalled);
            if (!showInstalled && !showCloud) statusMatch = true;

            bool sourceMatch = false;
            if (sourceSteam && g.Source.Contains("Steam")) sourceMatch = true;
            if (sourceXbox && g.Source.Contains("Xbox")) sourceMatch = true;
            if (sourceUbi && g.Source.Contains("Ubisoft")) sourceMatch = true;
            if (sourceEA && (g.Source.Contains("EA") || g.Source.Contains("Electronic Arts"))) sourceMatch = true;
            if (sourceEpic && g.Source.Contains("Epic")) sourceMatch = true;
            if (sourceBNet && g.Source.Contains("Battle.net")) sourceMatch = true;
            
            if (!sourceSteam && !sourceXbox && !sourceUbi && !sourceEA && !sourceEpic && !sourceBNet) sourceMatch = true;

            bool ownerMatch = false;
            if (stateClaimed && !g.IsNotRedeemed) ownerMatch = true;
            if (stateUnclaimed && g.IsNotRedeemed) ownerMatch = true;
            if (!stateClaimed && !stateUnclaimed) ownerMatch = true;

            bool textMatch = string.IsNullOrWhiteSpace(SearchBox?.Text) || g.Name.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase);
            bool notHidden = !_metadataService.IsHidden(g.GameData.Id);

            return statusMatch && sourceMatch && ownerMatch && textMatch && notHidden;
        });

        InstalledGames.Clear();
        foreach (var vm in filtered) InstalledGames.Add(vm);

        if (NoGamesText != null) NoGamesText.Visibility = InstalledGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsFilterChecked(string tag)
    {
        if (this.Content is Grid g)
        {
            foreach(var gridChild in g.Children)
            {
                if (gridChild is ScrollViewer sv && sv.Content is StackPanel sp)
                {
                    foreach(var child in sp.Children)
                    {
                        if (child is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton tb && tb.Tag?.ToString() == tag)
                        {
                            return tb.IsChecked ?? false;
                        }
                    }
                }
            }
        }
        return false;
    }

    private void SortByName_Click(object sender, RoutedEventArgs e) {
        bool asc = (sender as MenuFlyoutItem)?.Tag?.ToString() == "NameAsc";
        _allGames = asc ? _allGames.OrderBy(g => g.Name).ToList() : _allGames.OrderByDescending(g => g.Name).ToList();
        ApplyAdvancedFilters();
    }

    private void SortBySource_Click(object sender, RoutedEventArgs e) {
        _allGames = _allGames.OrderBy(g => g.Source).ThenBy(g => g.Name).ToList();
        ApplyAdvancedFilters();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ApplyAdvancedFilters(); }
    
    private void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e) 
    { 
        if (e.ClickedItem is LibraryGameViewModel vm) 
        {
            _lastSelectedIndex = LibraryGridView.Items.IndexOf(vm);
            this.Frame.Navigate(typeof(GameDetailsPage), vm); 
        }
    }
    
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) { ApplyAdvancedFilters(); }
    
    private void LibraryGridView_GettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (args.NewFocusedElement == LibraryGridView && LibraryGridView.Items.Count > 0)
        {
            int index = _lastSelectedIndex >= 0 ? _lastSelectedIndex : 0;
            var container = LibraryGridView.ContainerFromIndex(index) as Control;
            if (container != null) { args.TrySetNewFocusedElement(container); args.Handled = true; }
        }
    }

    private void LibraryGridView_LostFocus(object sender, RoutedEventArgs e) { }

    private async void LibraryGridView_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.GamepadX || e.Key == Windows.System.VirtualKey.X)
        {
            var focused = FocusManager.GetFocusedElement(this.XamlRoot);
            if (focused is GridViewItem gvi && gvi.Content is LibraryGameViewModel vm)
            {
                await _libraryService.LaunchGameAsync(vm.GameData);
            }
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
                    if (!string.IsNullOrEmpty(coverUrl)) { UpdateGameCover(vm, coverUrl); return; }
                }
            } catch { }
        }
    }

    private void UpdateGameCover(LibraryGameViewModel game, string url)
    {
        _metadataService.SetCover(game.GameData.Id, url);
        this.DispatcherQueue.TryEnqueue(() => { game.ImgCapsule = url; });
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

    public void FocusGameGrid()
    {
        this.DispatcherQueue.TryEnqueue(() => 
        {
            if (LibraryGridView.Items.Count > 0)
            {
                int index = _lastSelectedIndex >= 0 ? _lastSelectedIndex : 0;
                var container = LibraryGridView.ContainerFromIndex(index) as Control;
                container?.Focus(FocusState.Keyboard);
            }
            else
            {
                this.Focus(FocusState.Keyboard);
            }
        });
    }

    private void Log(string message) {
        try {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LibraryPage] {message}\n");
        } catch { }
    }
}
