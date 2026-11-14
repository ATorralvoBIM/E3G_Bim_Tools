using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Views.CopyLevels;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — Sprint 1: copia niveles desde un vinculo al
/// documento anfitrion (réplica funcional del CopyLevels de DiRootsOne).
///
/// La ventana se abre MODAL (ShowDialog) para que el usuario no pueda
/// modificar el modelo mientras decide qué niveles traer.  Tras cerrar, los
/// niveles ya estan creados y son visibles inmediatamente en Revit.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CopyLevelsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc is null)
        {
            TaskDialog.Show("Copiar niveles",
                "Abre un documento Revit antes de usar 'Copiar niveles'.");
            return Result.Cancelled;
        }

        try
        {
            var service = ServiceContainer.Resolve<ILevelCopyService>();
            var window = new CopyLevelsWindow(doc, service);

            // Anclamos la ventana a la ventana principal de Revit para que se
            // muestre encima y herede el comportamiento de minimizar/restaurar.
            var revitMainHandle = uiApp.MainWindowHandle;
            if (revitMainHandle != IntPtr.Zero)
            {
                _ = new WindowInteropHelper(window) { Owner = revitMainHandle };
            }
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
