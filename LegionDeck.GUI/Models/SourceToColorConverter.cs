using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LegionDeck.GUI.Models;

public class SourceToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string source)
        {
            Color color = source.ToLower() switch
            {
                "steam" => Color.FromArgb(255, 23, 26, 33),
                "xbox" => Color.FromArgb(255, 16, 124, 16),
                string s when s.Contains("not redeemed") => Color.FromArgb(255, 255, 140, 0), // Orange
                string s when s.Contains("ea") => Color.FromArgb(255, 255, 71, 71),
                "ubisoft" => Color.FromArgb(255, 0, 112, 255),
                "epic" => Color.FromArgb(255, 4, 150, 255), // Bright Epic blue
                "battle.net" => Color.FromArgb(255, 0, 174, 255), // Battle.net Blue
                _ => Color.FromArgb(255, 128, 128, 128)
            };
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string status) return status;
        return (value is bool isInstalled && isInstalled) ? "READY TO PLAY" : "IN CLOUD";
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class StatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string status && status == "NOT REDEEMED") return new SolidColorBrush(Color.FromArgb(255, 255, 140, 0));

        return (value is bool isInstalled && isInstalled) 
            ? new SolidColorBrush(Color.FromArgb(255, 46, 204, 113))
            : new SolidColorBrush(Color.FromArgb(255, 52, 152, 219));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
