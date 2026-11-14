using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.FamilyReviser;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 7: FamilyReviser.
/// Abre la ventana tabbed de FamilyReviser con las pestañas
/// Renombrar, Exportar a disco y Worksets.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class FamilyReviserCommand : IExternalCommand
{
    // Referencia viva a la ventana no-modal para evitar que el GC la recoja
    // mientras está abierta.
    private static FamilyReviserWindow? _openWindow;

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
            TaskDialog.Show("FamilyReviser", "Abre un documento Revit antes de usar FamilyReviser.");
            return Result.Cancelled;
        }

        try
        {
            // Si ya está abierta, traerla al frente en lugar de duplicarla.
            if (_openWindow is not null)
            {
                _openWindow.Activate();
                return Result.Succeeded;
            }

            var familyService  = ServiceContainer.Resolve<IFamilyReviserService>();
            var worksetService = ServiceContainer.Resolve<IWorksetService>();
            var dispatcher     = ServiceContainer.Resolve<IRevitUiDispatcher>();

            var window = new FamilyReviserWindow(uidoc, familyService, worksetService, dispatcher);

            var revitHandle = uiApp.MainWindowHandle;
            if (revitHandle != IntPtr.Zero)
                _ = new WindowInteropHelper(window) { Owner = revitHandle };

            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Closed += (_, _) => _openWindow = null;
            _openWindow = window;

            // Ventana NO modal: no bloquea Revit, el usuario puede seleccionar
            // y navegar en el modelo mientras está abierta. Las operaciones que
            // modifican el documento se marshalan al hilo de Revit vía ExternalEvent.
            window.Show();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
