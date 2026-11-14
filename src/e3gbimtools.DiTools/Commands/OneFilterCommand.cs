using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.OneFilter;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 2: OneFilter, filtra elementos del
/// documento por categoria + condiciones AND/OR anidadas y permite ejecutar
/// acciones (seleccionar, aislar, ocultar, override color, edicion en lote,
/// exportar CSV) sobre el resultado.
///
/// Ventana modal anclada a la principal de Revit.  Las acciones que modifican
/// el modelo abren sus propias transacciones via los servicios de Core.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class OneFilterCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;
        if (uidoc?.Document is null)
        {
            TaskDialog.Show("OneFilter",
                "Abre un documento Revit antes de usar OneFilter.");
            return Result.Cancelled;
        }

        try
        {
            var filterService = ServiceContainer.Resolve<IFilterService>();
            var templateService = ServiceContainer.Resolve<IFilterTemplateService>();
            var batchService = ServiceContainer.Resolve<IFilterBatchEditService>();
            var excelService = ServiceContainer.Resolve<IExcelService>();

            var window = new OneFilterWindow(uidoc, filterService, templateService, batchService, excelService);

            var revitMainHandle = uiApp.MainWindowHandle;
            if (revitMainHandle != IntPtr.Zero)
            {
                _ = new WindowInteropHelper(window) { Owner = revitMainHandle };
            }
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
