using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LegionDeck.Core.Services;
using System;
using System.Threading.Tasks;

namespace LegionDeck.GUI.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ConfigService _configService = new();
    private readonly SteamAuthService _steamAuth = new();
    private readonly XboxAuthService _xboxAuth = new();
    private readonly UbisoftAuthService _ubisoftAuth = new();

    public SettingsPage()
    {
        this.InitializeComponent();
        _configService = new ConfigService();
        _ubisoftAuth = new UbisoftAuthService(_configService);
        LoadSettings();
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Force focus to first input
        ItadKeyBox.Focus(FocusState.Programmatic);
    }

    private void Grid_GettingFocus(UIElement sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        // Redirect focus from the root grid to the first input
        if (args.NewFocusedElement == sender)
        {
            args.TrySetNewFocusedElement(ItadKeyBox);
            args.Handled = true;
        }
    }

    private void Settings_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Handle B (Escape) to go back
        if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
                e.Handled = true;
            }
        }
        // Explicitly let Menu bubble up or handle it if needed
        // Since MainPage handles it, we don't need to do anything here unless we want to block it.
    }

    private void LoadSettings()
    {
        ItadKeyBox.Password = _configService.GetApiKey("ITAD") ?? string.Empty;
        SgdbKeyBox.Password = _configService.GetApiKey("SGDB") ?? string.Empty;
        SteamApiKeyBox.Password = _configService.GetApiKey("STEAM") ?? string.Empty;
        IgdbClientIdBox.Text = _configService.GetApiKey("IGDB_CLIENT_ID") ?? string.Empty;
        IgdbClientSecretBox.Password = _configService.GetApiKey("IGDB_CLIENT_SECRET") ?? string.Empty;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var itadKey = ItadKeyBox.Password.Trim();
        var sgdbKey = SgdbKeyBox.Password.Trim();
        var steamKey = SteamApiKeyBox.Password.Trim();
        var igdbClientId = IgdbClientIdBox.Text.Trim();
        var igdbClientSecret = IgdbClientSecretBox.Password.Trim();

        _configService.SetApiKey("ITAD", itadKey);
        _configService.SetApiKey("SGDB", sgdbKey);
        _configService.SetApiKey("STEAM", steamKey);
        _configService.SetApiKey("IGDB_CLIENT_ID", igdbClientId);
        _configService.SetApiKey("IGDB_CLIENT_SECRET", igdbClientSecret);
        
        ShowInfoBar("Success", "API Keys saved. Restart app for changes to take effect fully.", InfoBarSeverity.Success);
    }

    private async void SteamLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _steamAuth.LoginAsync();
            if (result == "SteamLoggedIn")
            {
                ShowInfoBar("Steam", "Successfully authenticated with Steam.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Error", $"Steam Login failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void XboxLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _xboxAuth.LoginAsync();
            if (result == "XboxLoggedIn")
            {
                ShowInfoBar("Xbox", "Successfully authenticated with Xbox.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Error", $"Xbox Login failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void UbisoftLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _ubisoftAuth.LoginAsync();
            if (result == "UbisoftLoggedIn")
            {
                ShowInfoBar("Ubisoft", "Successfully authenticated with Ubisoft+.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Error", $"Ubisoft Login failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void EALogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var eaAuth = new EaAuthService();
            var result = await eaAuth.LoginAsync();
            if (result == "EALoggedIn")
            {
                // Reset flag to force fresh authenticated data on next scan
                LibraryUpdateService.ResetUpdateFlag();
                ShowInfoBar("EA", "Successfully authenticated with EA. You can now refresh your library.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Error", $"EA Login failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void EpicLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var epicAuth = new EpicAuthService();
            var result = await epicAuth.LoginAsync();
            if (!string.IsNullOrEmpty(result) && result.StartsWith("EpicCode:"))
            {
                LibraryUpdateService.ResetUpdateFlag();
                ShowInfoBar("Epic", "Successfully authenticated with Epic Games. You can now refresh your library.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Error", $"Epic Login failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void BattleNetLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bnetAuth = new BattleNetAuthService();
            var result = await bnetAuth.LoginAsync();
            if (result == "BattleNetLoggedIn")
            {
                LibraryUpdateService.ResetUpdateFlag();
                ShowInfoBar("Battle.net", "Successfully authenticated with Battle.net. You can now refresh your library.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Error", $"Battle.net Login failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void ShowInfoBar(string title, string message, InfoBarSeverity severity)
    {
        FeedbackInfoBar.Title = title;
        FeedbackInfoBar.Message = message;
        FeedbackInfoBar.Severity = severity;
        FeedbackInfoBar.IsOpen = true;
    }
}