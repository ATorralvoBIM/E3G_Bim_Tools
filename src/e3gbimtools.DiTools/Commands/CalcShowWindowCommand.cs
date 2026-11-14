// ============================================================================
//  CalcShowWindowCommand.cs
//  Comando Revit que abre la ventana CalcWindow (motor MEP Fase 5).
//  La ventana es no-modal: el usuario puede seguir trabajando en Revit.
// ============================================================================

using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Views;

namespace E3G.BimTools.DiTools.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CalcShowWindowCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document is null)
        {
            TaskDialog.Show("Cálculo MEP",
                "Abre un documento Revit antes de usar la ventana de cálculo.");
            return Result.Cancelled;
        }

        try
        {
            var calcEngine = ServiceContainer.Resolve<ICalcEngineService>();
            var critPath   = ServiceContainer.Resolve<ICriticalPathService>();
            var schematic  = ServiceContainer.Resolve<ISchematicLayoutService>();

            var win = new CalcWindow(commandData.Application, calcEngine, critPath, schematic);

            IntPtr revitHandle = commandData.Application.MainWindowHandle;
            if (revitHandle != IntPtr.Zero)
                new WindowInteropHelper(win) { Owner = revitHandle };

            win.Show();   // No modal: el usuario puede seguir interactuando con Revit
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Cálculo MEP", "Excepción al abrir la ventana:\n\n" + ex);
            return Result.Failed;
        }
    }
}
