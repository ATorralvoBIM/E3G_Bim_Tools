using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.ScheduleExcel;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — TableLink: abre la ventana para exportar
/// tablas de planificacion a Excel (como en Revit o en formato editable, una o
/// varias en pestañas) e importar los datos rellenados en Excel al modelo.
/// </summary>
// Manual: leer tablas (GetTableData) se hace en una transaccion que se revierte
// y la importacion escribe parametros en una transaccion propia.
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ScheduleExcelExportCommand : IExternalCommand
{
    private const string Title = "TableLink";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;
        if (uidoc?.Document is null || uidoc.Document.IsFamilyDocument)
        {
            TaskDialog.Show(Title, "Abre un proyecto de Revit antes de usar «TableLink».");
            return Result.Cancelled;
        }

        try
        {
            var service = ServiceContainer.Resolve<IScheduleExcelService>();
            var window = new ScheduleExcelWindow(uidoc, service);

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
