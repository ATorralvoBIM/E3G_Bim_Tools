using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.DialuxImport;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — DIALux → Revit: coloca en el modelo las
/// luminarias de un DWG exportado de DIALux, asociando cada bloque a un tipo de
/// familia de luminaria.  Sustituye a la rutina Dynamo DIALux_a_Revit.dyn.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class DialuxImportCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        Autodesk.Revit.DB.ElementSet elements)
    {
        try
        {
            return Abrir(commandData.Application) ? Result.Succeeded : Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    /// <summary>
    /// Abre la ventana modal.  Solo se usa desde la cinta cuando el panel MEP no
    /// cargo; con el panel, DIALux es una pagina suya (DialuxImportView).
    /// </summary>
    /// <returns>false si no hay un proyecto abierto.</returns>
    public static bool Abrir(UIApplication uiApp)
    {
        ServiceContainer.AttachUiApplication(uiApp);

        var uidoc = uiApp.ActiveUIDocument;
        if (uidoc?.Document is null)
        {
            TaskDialog.Show("DIALux → Revit", "Abre un documento Revit antes de usar «DIALux → Revit».");
            return false;
        }
        if (uidoc.Document.IsFamilyDocument)
        {
            TaskDialog.Show("DIALux → Revit", "La herramienta trabaja en proyectos, no en el editor de familias.");
            return false;
        }

        var service = ServiceContainer.Resolve<IDialuxImportService>();
        var window = new DialuxImportWindow(uiApp, service);

        var revitHandle = uiApp.MainWindowHandle;
        if (revitHandle != IntPtr.Zero)
            _ = new WindowInteropHelper(window) { Owner = revitHandle };

        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowDialog();
        return true;
    }
}
