using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.DxfLegend;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Leyenda desde DXF: recrea una leyenda de
/// AutoCAD (DXF/DWG) con dibujo de detalle nativo en la leyenda o vista de
/// dibujo elegida, con las mismas medidas a la escala de su ventana grafica.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class DxfLegendCommand : IExternalCommand
{
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
            TaskDialog.Show("Leyenda desde DXF", "Abre un documento Revit antes de usar «Leyenda desde DXF».");
            return Result.Cancelled;
        }
        if (uidoc.Document.IsFamilyDocument)
        {
            TaskDialog.Show("Leyenda desde DXF", "La herramienta trabaja en proyectos, no en el editor de familias.");
            return Result.Cancelled;
        }

        try
        {
            var service = ServiceContainer.Resolve<IDxfLegendService>();
            var window = new DxfLegendWindow(uidoc, service);

            var revitHandle = uiApp.MainWindowHandle;
            if (revitHandle != IntPtr.Zero)
                _ = new WindowInteropHelper(window) { Owner = revitHandle };

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
