using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace E3G.BimTools.DiTools.UI.Converters;

/// <summary>true -> Visible, false -> Collapsed (o Hidden si Parameter="Hidden").</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool x && x;
        var hidden = parameter is string s && s.Equals("Hidden", StringComparison.OrdinalIgnoreCase);
        return b ? Visibility.Visible : hidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}
