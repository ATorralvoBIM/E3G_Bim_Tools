// ============================================================================
//  ConverterToggleCommand.cs
//  BasePointView — Modo conversor de .rvt.
//
//  Botón «Conversor»: activa/desactiva que este Revit convierta los .rvt que
//  se suben al CDE de BasePointView (ver ConverterService).
// ============================================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.BimTools.BasePointExport
{
    /// <summary>
    /// El conversor no usa el documento activo: el botón debe estar disponible
    /// también en la pantalla de inicio de Revit (sin ningún archivo abierto).
    /// </summary>
    public sealed class AlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class ConverterToggleCommand : IExternalCommand
    {
        private const string Title = "BasePointView — Conversor de .rvt";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var service = ConverterService.Instance;
            if (service.IsRunning)
            {
                service.Stop();
                TaskDialog.Show(Title, "Conversor detenido. Los .rvt subidos quedarán en cola.");
                return Result.Succeeded;
            }

            var config = PublishConfig.LoadOrCreate();
            if (config.LoadError != null || string.IsNullOrWhiteSpace(config.ApiKey))
            {
                TaskDialog.Show(Title, config.MissingConfigMessage());
                ConverterService.Log("Configuración no válida: " + (config.LoadError ?? "apiKey vacía"));
                return Result.Cancelled;
            }

            service.Start();
            TaskDialog.Show(Title,
                "Conversor activo.\n\n" +
                "Este Revit convertirá los .rvt que se suban a BasePointView (" +
                config.BackendUrl + ") mientras siga abierto.\n\n" +
                "Durante cada conversión Revit estará ocupado unos minutos.\n" +
                "Registro: " + ConverterService.LogPath + "\n\n" +
                "Para activarlo siempre al abrir Revit, pon \"converterAutoStart\": true en " +
                PublishConfig.ConfigPath + ".");
            return Result.Succeeded;
        }
    }
}
