using System.Windows;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Views.OneParameter;

/// <summary>
/// Dialogo de Find &amp; Replace para OneParameter.  Devuelve FindText,
/// ReplaceText y MatchCase cuando el usuario acepta.
/// </summary>
public partial class FindReplaceDialog : Window
{
    public string FindText    { get; private set; } = string.Empty;
    public string ReplaceText { get; private set; } = string.Empty;
    public bool   MatchCase   { get; private set; }

    public FindReplaceDialog()
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        Loaded += (_, _) => FindBox.Focus();
    }

    private void OnReplace(object sender, RoutedEventArgs e)
    {
        var find = FindBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(find))
        {
            MessageBox.Show("Escribe el texto que quieres buscar.",
                "Find & Replace", MessageBoxButton.OK, MessageBoxImage.Information);
            FindBox.Focus();
            return;
        }

        FindText    = find;
        ReplaceText = ReplaceBox.Text ?? string.Empty;
        MatchCase   = MatchCaseCheck.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
