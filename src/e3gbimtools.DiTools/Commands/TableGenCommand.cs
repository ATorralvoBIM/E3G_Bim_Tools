using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.TableGen;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 8: TableGen parte 1.
/// Abre la ventana de importacion de tabla desde Excel hacia una
/// Drafting View de Revit.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class TableGenCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;

        if (uidoc?.Document is null)
        {
            TaskDialog.Show("TableGen", "Abre un documento Revit antes de usar TableGen.");
            return Result.Cancelled;
        }

        try
        {
            var service = ServiceContainer.Resolve<ITableGenService>();
            var window  = new TableGenWindow(uidoc, service);

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
