using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Views;
using E3G.BimTools.RevitBC3.Views;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando temporal "About" del panel DiTools (Sprint 0).  Se sustituira
/// por las herramientas reales a partir del Sprint 1.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AboutDiToolsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        // Actualiza la UIApplication del contexto por si todavia no estaba lista.
        ServiceContainer.AttachUiApplication(commandData.Application);

        var window = new AboutWindow(commandData.Application.Application.VersionNumber)
        {
            Owner = null  // no owner visible: usa centrado en pantalla
        };
        window.ShowDialog();
        return Result.Succeeded;
    }
}

/// <summary>
/// Comando temporal "About" del panel "Inicio Proyecto" (Sprint 0).
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AboutInicioProyectoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var window = new AboutWindow(commandData.Application.Application.VersionNumber);
        window.ShowDialog();
        return Result.Succeeded;
    }
}

/// <summary>
/// Comando del panel "Mediciones BC3": abre la ventana independiente
/// del modulo Revit -> BC3.
///
/// La ventana se abre en modo NO-MODAL (modeless): permanece flotando
/// encima de Revit pero deja al usuario seleccionar elementos, hacer
/// zoom y modificar el modelo sin tener que cerrarla.
///
/// Para evitar abrir dos copias, se guarda una referencia estatica de
/// la ventana abierta; si ya existe, se trae al frente.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class OpenRevitBC3Command : IExternalCommand
{
    // Instancia unica de la ventana mientras esta abierta.
    private static RevitBC3MainWindow? _openWindow;

    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        // Si ya hay una ventana abierta, la traemos al frente en lugar
        // de abrir una segunda.
        if (_openWindow is not null)
        {
            try
            {
                if (_openWindow.WindowState == WindowState.Minimized)
                    _openWindow.WindowState = WindowState.Normal;
                _openWindow.Activate();
                _openWindow.Focus();
                return Result.Succeeded;
            }
            catch
            {
                // La ventana habia quedado en un estado invalido.  La
                // descartamos y abrimos una nueva.
                _openWindow = null;
            }
        }

        // Pasamos la UIApplication para que la ventana pueda acceder al
        // documento activo (categorias / parametros del modelo).
        var window = new RevitBC3MainWindow(commandData.Application);

        // Establecemos la ventana principal de Revit como "padre" de la
        // ventana WPF.  Esto hace que se quede flotando encima de Revit
        // y minimice/maximice junto con el host, pero SIN bloquearlo.
        var revitMainHandle = commandData.Application.MainWindowHandle;
        if (revitMainHandle != IntPtr.Zero)
        {
            var helper = new WindowInteropHelper(window)
            {
                Owner = revitMainHandle,
            };
        }

        // Limpiar la referencia estatica al cerrarse.
        window.Closed += (s, e) => _openWindow = null;
        _openWindow = window;

        // Show() = modeless (no bloquea Revit).  ShowDialog() seria modal.
        window.Show();
        return Result.Succeeded;
    }
}
