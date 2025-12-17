using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;

namespace LegionDeck.GUI.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Set initial page
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag?.ToString() == "Library")
            {
                NavView.SelectedItem = item;
                ContentFrame.Navigate(typeof(LibraryPage));
                break;
            }
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItemContainer != null)
        {
            var tag = args.SelectedItemContainer.Tag.ToString();
            switch (tag)
            {
                case "Library":
                    ContentFrame.Navigate(typeof(LibraryPage));
                    break;
                case "Wishlist":
                    ContentFrame.Navigate(typeof(WishlistPage));
                    break;
                case "Subscriptions":
                    ContentFrame.Navigate(typeof(SubscriptionsPage));
                    break;
                case "Settings":
                    ContentFrame.Navigate(typeof(SettingsPage));
                    break;
            }
        }
    }
}