using System.Windows;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.RevitBC3.Views;

public partial class RuleDiagnosticDialog : Window
{
    public RuleDiagnosticDialog(string reportText)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        E3gWindowBase.SetTitle(this, "Diagnostico de regla");
        ReportBox.Text = reportText;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ReportBox.Text); } catch { /* sin ruido */ }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
