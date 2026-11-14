// ============================================================================
//  CalcSelfTestCommand.cs
//  Comando Revit que ejecuta el self-test del motor MEP y muestra el resultado
//  en un TaskDialog.  Util para validar la migración tras cada sprint.
// ============================================================================

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using e3gbimtools.Calc.Tests;

namespace E3G.BimTools.DiTools.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CalcSelfTestCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        try
        {
            var report = CalcSelfTest.Run();

            string headline = report.AllPassed
                ? "OK — " + report.Passed + "/" + report.Total + " tests superados."
                : "FALLO — " + report.Failed + " de " + report.Total + " tests no superados.";

            var dlg = new TaskDialog("e3gbimtools — MEP Calc Self-Test")
            {
                MainInstruction = headline,
                MainContent     = report.FullText,
                CommonButtons   = TaskDialogCommonButtons.Close,
                DefaultButton   = TaskDialogResult.Close,
                TitleAutoPrefix = false,
            };
            dlg.Show();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("MEP Calc Self-Test", "Excepción durante el self-test:\n\n" + ex);
            return Result.Failed;
        }
    }
}
