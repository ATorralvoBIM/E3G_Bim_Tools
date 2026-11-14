using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.SheetGen;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 6: SheetGen.  Abre la ventana
/// tabbed de SheetGen con las pestañas Crear/Importar, Layouts, Revisiones
/// y Auditoría/Renumerar.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SheetGenCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp  = commandData.Application;
        var uidoc  = uiApp.ActiveUIDocument;

        if (uidoc?.Document is null)
        {
            TaskDialog.Show("SheetGen", "Abre un documento Revit antes de usar SheetGen.");
            return Result.Cancelled;
        }

        try
        {
            var service = ServiceContainer.Resolve<ISheetGenService>();
            var window  = new SheetGenWindow(uidoc, service);

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
