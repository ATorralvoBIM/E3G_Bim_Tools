using System.Windows;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Views.OneFilter;

public partial class BatchEditDialog : Window
{
    public string ParameterName { get; private set; } = string.Empty;
    public string NewValue { get; private set; } = string.Empty;
    public bool ApplyToType { get; private set; }

    public BatchEditDialog()
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        ParameterBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ParameterName = ParameterBox.Text?.Trim() ?? string.Empty;
        NewValue = ValueBox.Text ?? string.Empty;
        ApplyToType = ApplyToTypeCheck.IsChecked == true;
        if (string.IsNullOrWhiteSpace(ParameterName))
        {
            MessageBox.Show(this, "Indica el nombre del parámetro.", "OneFilter",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }
}
