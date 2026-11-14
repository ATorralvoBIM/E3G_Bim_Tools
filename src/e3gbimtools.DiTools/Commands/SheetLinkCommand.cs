using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.SheetLink;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 11: SheetLink P1.
///
/// Abre <see cref="SheetLinkWindow"/> para exportar los datos de todos los
/// planos del modelo a un CSV editable en Excel, y luego reimportarlos para
/// actualizar numeros, nombres y parametros de revision en una sola transaccion.
///
/// La ventana es modal y esta anclada a la ventana principal de Revit.
/// Un unico Ctrl-Z deshace todos los cambios aplicados.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SheetLinkCommand : IExternalCommand
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
            TaskDialog.Show("SheetLink",
                "Abre un documento Revit antes de usar SheetLink.");
            return Result.Cancelled;
        }

        try
        {
            var sheetLinkService = ServiceContainer.Resolve<ISheetLinkService>();

            var window = new SheetLinkWindow(uidoc, sheetLinkService);

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
