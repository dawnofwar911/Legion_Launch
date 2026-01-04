using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using LegionDeck.GUI.Services;
using System.IO;

namespace LegionDeck.GUI.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
        Log("--- GUI BUILD VERSION 999.2 STARTED ---");
        this.Loaded += NavView_Loaded;
        if (Services.GamepadService.Instance != null)
        {
            Services.GamepadService.Instance.ButtonDown += OnGamepadButtonDown;
        }
    }

    private static readonly object _logLock = new object();
    private void Log(string message)
    {
        lock (_logLock)
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck");
                Directory.CreateDirectory(logDir);
                var path = Path.Combine(logDir, "startup.log");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [MainPage] {message}\n");
            }
            catch {{ }}
        }
    }

    private void OnGamepadButtonDown(object? sender, Services.GamepadService.GamepadButton button)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            try 
            {
                Log($"Gamepad Action: {button}");
                switch (button)
                {
                    case Services.GamepadService.GamepadButton.Menu:
                        NavView.IsPaneOpen = !NavView.IsPaneOpen;
                        if (NavView.IsPaneOpen) 
                        {
                            // Try to focus the selected item container
                            if (NavView.SelectedItem != null)
                            {
                                var container = NavView.ContainerFromMenuItem(NavView.SelectedItem) as Control;
                                if (container != null) 
                                {
                                    container.Focus(FocusState.Keyboard);
                                    break;
                                }
                            }
                            NavView.Focus(FocusState.Keyboard);
                        }
                        else 
                        {
                            if (ContentFrame.Content is LibraryPage libPage) libPage.FocusGameGrid();
                            else if (ContentFrame.Content is WishlistPage wishPage) { wishPage.Focus(FocusState.Keyboard); }
                            else ContentFrame.Focus(FocusState.Programmatic);
                        }
                        break;
                    case Services.GamepadService.GamepadButton.Up:
                    case Services.GamepadService.GamepadButton.Down:
                    case Services.GamepadService.GamepadButton.Left:
                    case Services.GamepadService.GamepadButton.Right:
                        // Recover focus if lost
                        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot) == null)
                        {
                            ContentFrame.Focus(FocusState.Programmatic);
                        }
                        
                        var dir = button switch 
                        {
                            Services.GamepadService.GamepadButton.Up => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up,
                            Services.GamepadService.GamepadButton.Down => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down,
                            Services.GamepadService.GamepadButton.Left => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Left,
                            _ => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right
                        };
                        
                        Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(dir, new Microsoft.UI.Xaml.Input.FindNextElementOptions { SearchRoot = this.XamlRoot.Content });
                        break;
                    case Services.GamepadService.GamepadButton.A:
                        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
                        if (focused is UIElement ui)
                        {
                            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(ui);
                            if (peer != null)
                            {
                                var invoke = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
                                if (invoke != null) 
                                {
                                    invoke.Invoke();
                                    return;
                                }

                                var toggle = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle) as Microsoft.UI.Xaml.Automation.Provider.IToggleProvider;
                                if (toggle != null)
                                {
                                    toggle.Toggle();
                                    return;
                                }
                                
                                var select = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.SelectionItem) as Microsoft.UI.Xaml.Automation.Provider.ISelectionItemProvider;
                                if (select != null)
                                {
                                    select.Select();
                                    return;
                                }
                            }
                        }
                        break;
                    case Services.GamepadService.GamepadButton.B:
                        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
                        else 
                        {
                            NavView.IsPaneOpen = !NavView.IsPaneOpen;
                            if (NavView.IsPaneOpen) NavView.Focus(FocusState.Programmatic);
                        }
                        break;
                }
            }
            catch (Exception ex) { Log($"Gamepad Handler Error: {ex.Message}"); }
        });
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var pageType = e.SourcePageType;
        string? tag = null;
        if (pageType == typeof(LibraryPage)) tag = "Library";
        else if (pageType == typeof(WishlistPage)) tag = "Wishlist";
        else if (pageType == typeof(SubscriptionsPage)) tag = "Subscriptions";
        else if (pageType == typeof(SettingsPage)) tag = "Settings";

        if (tag != null)
        {
            var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == tag) 
                    ?? NavView.FooterMenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
            
            if (item != null && NavView.SelectedItem != item)
            {
                NavView.SelectedItem = item;
            }
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag?.ToString() == "Library")
            {
                NavView.SelectedItem = item;
                ContentFrame.Navigate(typeof(LibraryPage));
                
                // Try to focus content first
                if (ContentFrame.Content is LibraryPage libPage)
                {
                    libPage.FocusGameGrid();
                }
                else
                {
                    ContentFrame.Focus(FocusState.Programmatic);
                }
                break;
            }
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) ContentFrame.Navigate(typeof(SettingsPage));
        else if (args.SelectedItemContainer != null)
        {
            var tag = args.SelectedItemContainer.Tag.ToString();
            switch (tag)
            {
                case "Library": ContentFrame.Navigate(typeof(LibraryPage)); break;
                case "Wishlist": ContentFrame.Navigate(typeof(WishlistPage)); break;
                case "Subscriptions": ContentFrame.Navigate(typeof(SubscriptionsPage)); break;
                case "Settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
            }
        }
    }

    private void Grid_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
        {
            if (ContentFrame.CanGoBack) { ContentFrame.GoBack(); e.Handled = true; } 
            else { NavView.IsPaneOpen = true; NavView.Focus(FocusState.Keyboard); e.Handled = true; }
        }
        else if (e.Key == Windows.System.VirtualKey.GamepadMenu || e.Key == Windows.System.VirtualKey.M)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
            if (NavView.IsPaneOpen) NavView.Focus(FocusState.Keyboard); else ContentFrame.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }
}