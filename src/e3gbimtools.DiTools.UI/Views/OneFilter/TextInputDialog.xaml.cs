using System.Windows;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Views.OneFilter;

/// <summary>
/// Mini dialogo para pedir una cadena al usuario (nombre de plantilla, etc).
/// Sustituye al InputBox de VB sin depender de la libreria Microsoft.VisualBasic.
/// </summary>
public partial class TextInputDialog : Window
{
    public string Result { get; private set; } = string.Empty;

    public TextInputDialog(string title, string label, string initial)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        Title = title;
        LabelText.Text = label;
        InputBox.Text = initial ?? string.Empty;
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = InputBox.Text ?? string.Empty;
        DialogResult = true;
        Close();
    }
}
