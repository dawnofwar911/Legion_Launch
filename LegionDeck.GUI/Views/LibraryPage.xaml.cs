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
            if (InstalledGames.Count == 0) await RefreshLibraryAsync();
            
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
            InstalledGames.Clear();
            var games = await _libraryService.GetInstalledGamesAsync();
            Log($"Found {games.Count} games");
            foreach (var game in games)
            {
                InstalledGames.Add(new LibraryGameViewModel(game));
            }
            Log("RefreshLibraryAsync completed");
        }
        catch (Exception ex)
        {
            Log($"Error in RefreshLibraryAsync: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LibraryGameViewModel vm)
        {
            await _libraryService.LaunchGameAsync(vm.GameData);
        }
    }
}