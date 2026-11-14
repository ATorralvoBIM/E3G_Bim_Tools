using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.ParaManager;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 3: ParaManager.  Abre la ventana
/// modal con las cuatro pestañas (lista, crear, shared parameter file,
/// editar en lote).  Las acciones que modifican el modelo abren sus propias
/// transacciones via los servicios de Core.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ParaManagerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;
        if (uidoc?.Document is null)
        {
            TaskDialog.Show("ParaManager", "Abre un documento Revit antes de usar ParaManager.");
            return Result.Cancelled;
        }

        try
        {
            var paraManager = ServiceContainer.Resolve<IParaManagerService>();
            var filter = ServiceContainer.Resolve<IFilterService>();
            var bulkEdit = ServiceContainer.Resolve<IParameterValueBulkEditService>();

            var window = new ParaManagerWindow(uidoc, paraManager, filter, bulkEdit);

            var revitMainHandle = uiApp.MainWindowHandle;
            if (revitMainHandle != IntPtr.Zero)
                _ = new WindowInteropHelper(window) { Owner = revitMainHandle };

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
