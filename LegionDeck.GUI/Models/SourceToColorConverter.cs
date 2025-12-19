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
                "ea" => Color.FromArgb(255, 255, 71, 71),
                "ubisoft" => Color.FromArgb(255, 0, 112, 255),
                "epic" => Color.FromArgb(255, 51, 51, 51),
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
