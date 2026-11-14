using System.Windows;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.RevitBC3.Views;

/// <summary>
/// Sprint B — Dialogo modal compartido para crear / renombrar capitulos y
/// subcapitulos.  Recoge dos campos (Codigo, Nombre) y devuelve DialogResult
/// true si el usuario acepta.  No valida unicidad: eso lo hace el llamador
/// con conocimiento del contexto (mismo capitulo no puede tener dos subcaps
/// con el mismo codigo, etc.).
/// </summary>
public partial class ChapterEditorDialog : Window
{
    public string CodeValue { get; private set; } = string.Empty;
    public string NameValue { get; private set; } = string.Empty;

    public ChapterEditorDialog(string title, string initialCode, string initialName)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        TitleText.Text = title;
        Title = title;
        CodeBox.Text = initialCode ?? string.Empty;
        NameBox.Text = initialName ?? string.Empty;
        CodeBox.Focus();
        CodeBox.SelectAll();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        CodeValue = (CodeBox.Text ?? string.Empty).Trim();
        NameValue = (NameBox.Text ?? string.Empty).Trim();

        if (CodeValue.Length == 0 && NameValue.Length == 0)
        {
            MessageBox.Show(this,
                "Introduce al menos un codigo o un nombre.",
                "Datos incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
