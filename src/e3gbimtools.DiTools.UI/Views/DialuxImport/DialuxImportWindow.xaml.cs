using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Hosting;

namespace E3G.BimTools.DiTools.UI.Views.DialuxImport;

/// <summary>
/// Ventana modal de "DIALux → Revit" para el boton de la cinta, que solo existe si
/// el panel MEP no cargo (p. ej. Revit 2025).  Aloja <see cref="DialuxImportView"/>
/// con llamadas directas a la API: al ser modal y abrirse desde un comando, sigue
/// en contexto de API mientras esta abierta.
/// </summary>
public partial class DialuxImportWindow : Window
{
    private readonly DialuxImportView _view;

    public DialuxImportWindow(UIApplication uiApp, IDialuxImportService service)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        _view = new DialuxImportView(service, new DirectRevitInvoker(uiApp), integrada: false);
        Host.Content = _view;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _view.GuardarAjustes();
        base.OnClosing(e);
    }
}
