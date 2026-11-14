// ============================================================================
//  CmdShowCalcWindow.cs
//  Comando Revit que abre la ventana CalcWindow (Sprint 5).
//  Sustituye los TaskDialog rapidos por una UI WPF completa.
// ============================================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Windows.Interop;

namespace E3G.Saneamiento
{
    [Transaction(TransactionMode.Manual)]
    public class CmdShowCalcWindow : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc   = uidoc?.Document;
                if (doc == null)
                {
                    message = "No hay documento activo.";
                    return Result.Failed;
                }

                // [Fase 1] Sincronización bidireccional Revit ↔ UI:
                // pasamos UIApplication a la nueva sobrecarga y abrimos la
                // ventana NO MODAL para que Revit pueda entregar eventos
                // SelectionChanged mientras MEPCalc está abierta.
                var win = new CalcWindow(doc, commandData.Application);

                // La ventana se parenta al main handle de Revit (sigue
                // apareciendo encima de Revit y se cierra con él) pero NO
                // bloquea — el usuario puede seguir interactuando con el
                // modelo en paralelo.
                IntPtr revitHandle = commandData.Application.MainWindowHandle;
                if (revitHandle != IntPtr.Zero)
                {
                    var helper = new WindowInteropHelper(win) { Owner = revitHandle };
                }

                win.Show();                    // antes ShowDialog (modal)
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MEP Calc - Ventana",
                    "Excepcion al abrir la ventana de calculo:\n\n" + ex);
                return Result.Failed;
            }
        }
    }
}
