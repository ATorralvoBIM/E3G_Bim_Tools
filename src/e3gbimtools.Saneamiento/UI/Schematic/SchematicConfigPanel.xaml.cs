// ============================================================================
//  SchematicConfigPanel.xaml.cs
//  Code-behind mínimo del UserControl — solo InitializeComponent.
//  Toda la lógica vive en SchematicConfigViewModel (MVVM).
//  El host fija el DataContext:
//      myPanel.DataContext = new SchematicConfigViewModel();
// ============================================================================
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace E3G.Saneamiento.UI.Schematic
{
    public partial class SchematicConfigPanel : UserControl
    {
        public SchematicConfigPanel()
        {
            InitializeComponent();
        }
    }

    /// <summary>
    /// Formatea un double con dos decimales + sufijo opcional (parámetro).
    /// Usado por los TextBlock que muestran el valor actual del slider:
    ///   8.00 mm
    /// </summary>
    public sealed class DoubleFormatConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter,
            CultureInfo culture)
        {
            if (value is double d)
            {
                var unit = parameter as string;
                return string.IsNullOrEmpty(unit)
                    ? d.ToString("F2", CultureInfo.InvariantCulture)
                    : d.ToString("F1", CultureInfo.InvariantCulture) + " " + unit;
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, System.Type targetType, object parameter,
            CultureInfo culture) => Binding.DoNothing;
    }
}
