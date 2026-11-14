// ============================================================================
//  HexToColorConverter — convierte un string hex "#RRGGBB" en System.Windows.Media.Color
//  para que el binding del XAML pueda alimentar un SolidColorBrush.
//
//  Usado por el DataGrid de Ajustes › Classificació de sistemes para pintar
//  el cuadrito de color de cada PipingSystemType (color leído de Revit en
//  FilaClasificacion.ColorRevit).
//
//  Tolera entradas vacías o malformadas → devuelve gris neutro #94A3B8.
// ============================================================================

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace E3G.Saneamiento
{
    public sealed class HexToColorConverter : IValueConverter
    {
        private static readonly Color Fallback =
            Color.FromRgb(0x94, 0xA3, 0xB8);   // gris neutro

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var hex = (value as string)?.Trim();
            if (string.IsNullOrEmpty(hex)) return Fallback;

            if (hex.StartsWith("#")) hex = hex.Substring(1);

            // Soporta tanto "RRGGBB" como "AARRGGBB"
            try
            {
                if (hex.Length == 6)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return Color.FromRgb(r, g, b);
                }
                if (hex.Length == 8)
                {
                    byte a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch { /* malformado → fallback */ }

            return Fallback;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("HexToColorConverter es one-way.");
    }
}
