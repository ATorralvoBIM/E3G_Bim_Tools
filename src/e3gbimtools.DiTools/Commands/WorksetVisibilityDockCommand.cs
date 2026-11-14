using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.UI.Views.Worksets;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — abre el dockable pane de visibilidad de
/// worksets (Bloque E). El panel es modeless: una vez mostrado se puede fijar
/// en el UI de Revit y sigue la vista activa en vivo.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class WorksetVisibilityDockCommand : IExternalCommand
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
            TaskDialog.Show("Visibilidad de worksets", "Abre un documento Revit primero.");
            return Result.Cancelled;
        }

        try
        {
            var pane = uiApp.GetDockablePane(WorksetDock.PaneId);
            pane?.Show();

            // Estamos en contexto API válido: refrescar al instante con la vista actual.
            WorksetDock.RequestRefresh();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
