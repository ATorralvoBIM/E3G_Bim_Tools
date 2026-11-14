using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.SectionBoxer;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 5: SectionBoxer.  Abre la ventana
/// modal con el editor de section boxes 3D.  Las acciones que modifican el
/// modelo abren sus propias transacciones via los servicios de Core.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SectionBoxerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;
        if (uidoc?.Document is null)
        {
            TaskDialog.Show("SectionBoxer", "Abre un documento Revit antes de usar SectionBoxer.");
            return Result.Cancelled;
        }

        try
        {
            var sectionBoxer = ServiceContainer.Resolve<ISectionBoxService>();
            var filterTemplates = ServiceContainer.Resolve<IFilterTemplateService>();

            var window = new SectionBoxerWindow(uidoc, sectionBoxer, filterTemplates);

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
