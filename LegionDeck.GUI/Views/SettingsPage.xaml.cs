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

    public SettingsPage()
    {
        this.InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        ItadKeyBox.Text = _configService.GetApiKey("ITAD") ?? string.Empty;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var key = ItadKeyBox.Text.Trim();
        if (!string.IsNullOrEmpty(key))
        {
            _configService.SetApiKey("ITAD", key);
            ShowInfoBar("Success", "ITAD API Key saved.", InfoBarSeverity.Success);
        }
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

    private void ShowInfoBar(string title, string message, InfoBarSeverity severity)
    {
        // We'll add an InfoBar to the XAML for feedback
        FeedbackInfoBar.Title = title;
        FeedbackInfoBar.Message = message;
        FeedbackInfoBar.Severity = severity;
        FeedbackInfoBar.IsOpen = true;
    }
}
