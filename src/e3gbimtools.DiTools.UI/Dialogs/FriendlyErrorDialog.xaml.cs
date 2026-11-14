using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using E3G.BimTools.DiTools.Core;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Dialogs;

/// <summary>
/// Dialogo amistoso para excepciones no controladas.  Hereda de Window
/// estandar; el tema e3g se aplica via <see cref="E3gWindowBase.ApplyTheme"/>.
/// </summary>
public partial class FriendlyErrorDialog : Window
{
    private string _technicalDetails = string.Empty;

    public FriendlyErrorDialog()
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        E3gWindowBase.SetTitle(this, "Error inesperado");
    }

    /// <summary>Crea el dialogo a partir de una excepcion y la formatea.</summary>
    public static FriendlyErrorDialog ForException(Exception ex)
    {
        var dlg = new FriendlyErrorDialog();
        dlg.MessageText.Text =
            "Se ha producido un error inesperado. El detalle tecnico se ha guardado " +
            "en el archivo de log; puedes copiarlo al portapapeles o abrir la carpeta " +
            "de logs para enviarselo al equipo.";
        dlg._technicalDetails = $"{ex.GetType().FullName}: {ex.Message}\r\n\r\n{ex}";
        dlg.DetailsBox.Text = dlg._technicalDetails;
        return dlg;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_technicalDetails); } catch { /* ignore */ }
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
