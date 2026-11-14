using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.OneParameter;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 10: OneParameter.
///
/// Abre <see cref="OneParameterWindow"/> para buscar todos los elementos del
/// modelo que poseen un parametro dado, ver sus valores, editarlos inline o
/// en lote y ejecutar Find &amp; Replace sobre valores de texto.
///
/// La ventana es modal y esta anclada a la ventana principal de Revit.
/// Las operaciones de escritura abren sus propias transacciones; un unico
/// Ctrl-Z deshace todo lo aplicado en cada sesion de edicion.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class OneParameterCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string          message,
        Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;

        if (uidoc?.Document is null)
        {
            TaskDialog.Show("OneParameter",
                "Abre un documento Revit antes de usar OneParameter.");
            return Result.Cancelled;
        }

        try
        {
            var oneParamService = ServiceContainer.Resolve<IOneParameterService>();
            var filterService   = ServiceContainer.Resolve<IFilterService>();

            var window = new OneParameterWindow(uidoc, oneParamService, filterService);

            var revitHandle = uiApp.MainWindowHandle;
            if (revitHandle != IntPtr.Zero)
                _ = new WindowInteropHelper(window) { Owner = revitHandle };

            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowDialog();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
