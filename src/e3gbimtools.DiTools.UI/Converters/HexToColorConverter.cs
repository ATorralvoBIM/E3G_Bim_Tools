using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace E3G.BimTools.DiTools.UI.Converters;

/// <summary>
/// String hex tipo "#4A7A96" -&gt; SolidColorBrush.
/// Heredado del proyecto Saneamiento; util para colorear botones dinamicamente.
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return Brushes.Transparent;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(s)!;
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SolidColorBrush b ? b.Color.ToString() : string.Empty;
}
