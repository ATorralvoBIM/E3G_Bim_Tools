// ============================================================================
//  CmdMepCalcSelfTest.cs
//  Comando Revit que ejecuta el self-test del motor de calculo MEP y muestra
//  el resultado en un TaskDialog. Para pruebas manuales del usuario tras
//  cada sprint, antes de avanzar al siguiente.
//
//  Como lanzarlo desde Revit 2025:
//    Ribbon -> E3G BIM Tools -> "MEP Calc Self-Test"
//    o bien: Add-Ins -> External Tools -> "MEP Calc Self-Test"
// ============================================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using E3G.Saneamiento.Tests;

namespace E3G.Saneamiento
{
    [Transaction(TransactionMode.Manual)]
    public class CmdMepCalcSelfTest : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var report = CalcSelfTest.Run();

                string headline = report.AllPassed
                    ? "OK - " + report.Passed + "/" + report.Total + " tests superados."
                    : "FALLO - " + report.Failed + " de " + report.Total + " tests no superados.";

                var dlg = new TaskDialog("MEP Calc - Self-Test (Sprint 1-6)")
                {
                    MainInstruction = headline,
                    MainContent     = report.FullText,
                    CommonButtons   = TaskDialogCommonButtons.Close,
                    DefaultButton   = TaskDialogResult.Close,
                    TitleAutoPrefix = false,
                };
                dlg.Show();

                // Devolvemos Succeeded incluso si algun test falla: el detalle
                // ya se muestra al usuario en el TaskDialog.  Result.Failed se
                // reserva para excepciones reales del comando.
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MEP Calc - Self-Test", "Excepcion durante el self-test:\n\n" + ex);
                return Result.Failed;
            }
        }
    }
}
