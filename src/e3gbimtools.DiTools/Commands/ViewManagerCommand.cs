using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.ViewManager;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 4: ViewManager.  Abre la ventana
/// modal con las pestañas (Listado, Aplicar, View Range, Diff).  Las
/// acciones que modifican el modelo abren sus propias transacciones via
/// los servicios de Core.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ViewManagerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;
        if (uidoc?.Document is null)
        {
            TaskDialog.Show("ViewManager", "Abre un documento Revit antes de usar ViewManager.");
            return Result.Cancelled;
        }

        try
        {
            var templates = ServiceContainer.Resolve<IViewTemplateService>();
            var ranges    = ServiceContainer.Resolve<IViewRangeService>();
            var comparer  = ServiceContainer.Resolve<IViewTemplateComparer>();

            var window = new ViewManagerWindow(uidoc, templates, ranges, comparer);

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
