using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.UI.Views.FilterGraphics;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — abre el dockable pane "Grafismo de filtros"
/// (exportar/importar líneas, patrones y transparencia de los filtros de V/G).
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class FilterGraphicsDockCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        if (uiApp.ActiveUIDocument?.Document is null)
        {
            TaskDialog.Show(FilterGraphicsDock.Title, "Abre un documento Revit primero.");
            return Result.Cancelled;
        }

        try
        {
            uiApp.GetDockablePane(FilterGraphicsDock.PaneId)?.Show();
            FilterGraphicsDock.RequestRefresh();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
