using System.Diagnostics;
using System.IO;
using System.Windows;
using E3G.BimTools.DiTools.Core;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.Views;

public partial class AboutWindow : Window
{
    public AboutWindow(string revitVersion)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        E3gWindowBase.SetTitle(this, "Acerca de");

        VersionRun.Text = ProjectInfo.AssemblyVersion;
        RevitRun.Text = string.IsNullOrWhiteSpace(revitVersion) ? "—" : revitVersion;
        LogPathText.Text = ProjectInfo.LogsFolder;
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = ProjectInfo.LogsFolder;
            if (Directory.Exists(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch { /* no romper */ }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
