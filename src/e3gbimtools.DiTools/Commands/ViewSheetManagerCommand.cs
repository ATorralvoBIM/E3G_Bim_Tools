using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Services.ViewSheets;
using E3G.BimTools.DiTools.UI.Views.ViewSheetManager;

namespace E3G.BimTools.DiTools.Commands;

/// <summary>
/// Comando del panel "DiTools" — módulo "View &amp; Sheet Manager".  Abre la
/// ventana modal que automatiza la creación de planos de instalaciones MEP
/// (doble bucle instalaciones × niveles).  La generación abre su propia
/// transacción vía <see cref="IViewSheetGenerator"/> en Core.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ViewSheetManagerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;
        if (uidoc?.Document is null)
        {
            TaskDialog.Show("View & Sheet Manager", "Abre un documento Revit antes de usar View & Sheet Manager.");
            return Result.Cancelled;
        }

        try
        {
            var generator = ServiceContainer.Resolve<IViewSheetGenerator>();
            var repository = ServiceContainer.Resolve<InstalacionConfigRepository>();
            var paraManager = ServiceContainer.Resolve<IParaManagerService>();

            var window = new ViewSheetManagerWindow(uidoc, generator, repository, paraManager);

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
