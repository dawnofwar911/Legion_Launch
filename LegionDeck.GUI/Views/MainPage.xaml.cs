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
        ContentFrame.Navigated += ContentFrame_Navigated;
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Sync NavView selection with current page
        var pageType = e.SourcePageType;
        string? tag = null;

        if (pageType == typeof(LibraryPage)) tag = "Library";
        else if (pageType == typeof(WishlistPage)) tag = "Wishlist";
        else if (pageType == typeof(SubscriptionsPage)) tag = "Subscriptions";
        else if (pageType == typeof(SettingsPage)) tag = "Settings";

        if (tag != null)
        {
            var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
            if (item == null)
            {
                item = NavView.FooterMenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
            }

            if (item != null)
            {
                NavView.SelectedItem = item;
            }
        }
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
            else
            {
                // If at root of page, focus NavView
                NavView.IsPaneOpen = true;
                NavView.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }
        // Handle Menu (Mapped to M) or Native GamepadMenu
        else if (e.Key == Windows.System.VirtualKey.GamepadMenu || e.Key == Windows.System.VirtualKey.M)
        {
            NavView.IsPaneOpen = true;
            
            // Wait for pane to open then focus
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                bool focused = false;

                // Try to focus selected item container
                if (NavView.SelectedItem != null)
                {
                    var container = NavView.ContainerFromMenuItem(NavView.SelectedItem) as Control;
                    focused = container?.Focus(FocusState.Programmatic) ?? false;
                }

                // Fallback: Focus Settings item container (Priority over First Item if selected was null/failed but meant to be Settings)
                if (!focused)
                {
                     var settingsItem = NavView.FooterMenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Settings");
                     if (settingsItem != null && NavView.SelectedItem == settingsItem)
                     {
                         var container = NavView.ContainerFromMenuItem(settingsItem) as Control;
                         focused = container?.Focus(FocusState.Programmatic) ?? false;
                     }
                }

                // Fallback: Focus first menu item container
                if (!focused && NavView.MenuItems.Count > 0)
                {
                    var firstItem = NavView.MenuItems[0];
                    var container = NavView.ContainerFromMenuItem(firstItem) as Control;
                    focused = container?.Focus(FocusState.Programmatic) ?? false;
                }

                // Final Fallback: Focus NavView itself (better than nothing)
                if (!focused)
                {
                    NavView.Focus(FocusState.Programmatic);
                }
            };
            timer.Start();
            
            e.Handled = true;
        }
    }
}