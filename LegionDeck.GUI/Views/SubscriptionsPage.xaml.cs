using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LegionDeck.Core.Services;
using System;
using System.Threading.Tasks;

namespace LegionDeck.GUI.Views;

public sealed partial class SubscriptionsPage : Page
{
    private readonly XboxDataService _xboxData = new();
    private readonly EaDataService _eaData = new();
    private readonly UbisoftDataService _ubisoftData = new();

    public SubscriptionsPage()
    {
        this.InitializeComponent();
        this.Loaded += SubscriptionsPage_Loaded;
    }

    private async void SubscriptionsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAllStatus();
    }

    private async Task RefreshAllStatus()
    {
        // Xbox
        XboxInfo.Message = "Checking...";
        var xboxStatus = await _xboxData.GetGamePassSubscriptionDetailsAsync();
        XboxInfo.Message = xboxStatus;
        XboxInfo.Severity = xboxStatus.Contains("Game Pass") ? InfoBarSeverity.Success : InfoBarSeverity.Informational;

        // EA
        EaInfo.Message = "Checking...";
        var eaStatus = await _eaData.GetEaPlaySubscriptionDetailsAsync();
        EaInfo.Message = eaStatus;
        EaInfo.Severity = eaStatus.Contains("EA Play") ? InfoBarSeverity.Success : InfoBarSeverity.Informational;

        // Ubisoft
        UbiInfo.Message = "Checking...";
        var ubiStatus = await _ubisoftData.GetUbisoftPlusSubscriptionDetailsAsync();
        UbiInfo.Message = ubiStatus;
        UbiInfo.Severity = ubiStatus.Contains("Ubisoft+") ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
    }
}
