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
        if (InstalledGames.Count == 0) await RefreshLibraryAsync();
        
        await Task.Delay(200);
        LibraryGridView.Focus(FocusState.Programmatic);
        if (InstalledGames.Any()) LibraryGridView.SelectedIndex = 0;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshLibraryAsync();

    private async Task RefreshLibraryAsync()
    {
        InstalledGames.Clear();
        var games = await _libraryService.GetInstalledGamesAsync();
        foreach (var game in games)
        {
            InstalledGames.Add(new LibraryGameViewModel(game));
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