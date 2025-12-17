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

    public LibraryPage()
    {
        this.InitializeComponent();
        this.AllowFocusOnInteraction = true;
        LibraryGridView.ItemsSource = InstalledGames;
        this.Loaded += LibraryPage_Loaded;
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log("LibraryPage_Loaded started");
            if (_allGames.Count == 0) await RefreshLibraryAsync();
            
            await Task.Delay(200);
            Log("Setting focus to LibraryGridView");
            LibraryGridView.Focus(FocusState.Programmatic);
            if (InstalledGames.Any()) LibraryGridView.SelectedIndex = 0;
            Log("LibraryPage_Loaded completed");
        }
        catch (Exception ex)
        {
            Log($"Error in LibraryPage_Loaded: {ex.Message}\n{ex.StackTrace}");
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
                _allGames.Add(new LibraryGameViewModel(game));
            }
            ApplyFilter(SearchBox.Text);
            Log("RefreshLibraryAsync completed");
        }
        catch (Exception ex)
        {
            Log($"Error in RefreshLibraryAsync: {ex.Message}\n{ex.StackTrace}");
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