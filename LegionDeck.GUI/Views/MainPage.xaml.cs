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
                
                // Force focus to the NavView so controller has a starting point
                NavView.Focus(FocusState.Programmatic);
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

    private void Grid_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Handle B (Mapped to Escape) or Native GamepadB
        if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
        {
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
                e.Handled = true;
            }
        }
        // Handle Menu (Mapped to M) or Native GamepadMenu
        else if (e.Key == Windows.System.VirtualKey.GamepadMenu || e.Key == Windows.System.VirtualKey.M)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
            NavView.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }
}