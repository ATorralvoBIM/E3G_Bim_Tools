// ============================================================================
//  BasePointViewApp.cs
//  BasePointView — Exportador Revit (Sprint 4).
//
//  IExternalApplication que añade al ribbon una pestaña «BasePointView» con el
//  botón de exportación y su logo.  Es la forma cómoda de lanzar el comando,
//  además del menú Complementos → Comandos externos.
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace E3G.BimTools.BasePointExport
{
    public sealed class BasePointViewApp : IExternalApplication
    {
        private const string TabName = "BasePointView";

        public Result OnStartup(UIControlledApplication application)
        {
            // La pestaña puede existir ya si se carga junto a otros add-ins E3G.
            try { application.CreateRibbonTab(TabName); }
            catch (Exception) { /* la pestaña ya existe */ }

            var panel = application.CreateRibbonPanel(TabName, "Exportar");
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var resources = Path.Combine(
                Path.GetDirectoryName(assemblyPath) ?? string.Empty, "Resources");

            var export = new PushButtonData(
                "BpvExportToGlb",
                "Exportar\na GLB",
                assemblyPath,
                "E3G.BimTools.BasePointExport.ExportCommand")
            {
                ToolTip = "Exporta la vista 3D activa a GLB + JSON (archivos).",
                LongDescription =
                    "Genera geometría (GLB) y propiedades por UniqueId (JSON) sin " +
                    "dependencia de Autodesk APS/Forge. Arrastra ambos archivos al visor web.",
                LargeImage = LoadImage(Path.Combine(resources, "bpv-export-32.png")),
                Image = LoadImage(Path.Combine(resources, "bpv-export-16.png")),
            };

            var publish = new PushButtonData(
                "BpvPublish",
                "Publicar\nal visor",
                assemblyPath,
                "E3G.BimTools.BasePointExport.PublishCommand")
            {
                ToolTip = "Sube la vista 3D activa al visor web, sin manejar archivos.",
                LongDescription =
                    "Exporta GLB + JSON y los publica en el backend de BasePointView. " +
                    "El modelo aparece versionado en el catálogo del visor.",
                LargeImage = LoadImage(Path.Combine(resources, "bpv-publish-32.png")),
                Image = LoadImage(Path.Combine(resources, "bpv-publish-16.png")),
            };

            var publishSheet = new PushButtonData(
                "BpvPublishSheet",
                "Publicar\nlámina",
                assemblyPath,
                "E3G.BimTools.BasePointExport.PublishSheetCommand")
            {
                ToolTip = "Publica la vista 2D activa (planta) como lámina georreferenciada.",
                LongDescription =
                    "Exporta la vista 2D activa a PNG y la sube al visor situada en su " +
                    "posición y cota reales sobre el modelo 3D (estilo Dalux). La " +
                    "georreferencia se toma de la región de recorte de la vista.",
                LargeImage = LoadImage(Path.Combine(resources, "bpv-publish-32.png")),
                Image = LoadImage(Path.Combine(resources, "bpv-publish-16.png")),
            };

            panel.AddItem(export);
            panel.AddItem(publish);
            panel.AddItem(publishSheet);

            var converterPanel = application.CreateRibbonPanel(TabName, "Conversor");
            var converter = new PushButtonData(
                "BpvConverter",
                "Conversor\n.rvt",
                assemblyPath,
                "E3G.BimTools.BasePointExport.ConverterToggleCommand")
            {
                ToolTip = "Activa/desactiva que este Revit convierta los .rvt subidos al visor.",
                LongDescription =
                    "Mientras está activo, este Revit descarga los .rvt subidos al CDE de " +
                    "BasePointView, los abre en segundo plano y publica modelo 3D, láminas, " +
                    "vistas y tablas de planificación. Quien consulta no necesita Revit.",
                LargeImage = LoadImage(Path.Combine(resources, "bpv-publish-32.png")),
                Image = LoadImage(Path.Combine(resources, "bpv-publish-16.png")),
                AvailabilityClassName = "E3G.BimTools.BasePointExport.AlwaysAvailable",
            };
            if (converterPanel.AddItem(converter) is PushButton button)
            {
                ConverterService.Instance.RunningChanged += running =>
                    button.ItemText = running ? "Conversor\nactivo" : "Conversor\n.rvt";
            }

            if (PublishConfig.LoadOrCreate().ConverterAutoStart)
            {
                ConverterService.Instance.Start();
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            ConverterService.Instance.Stop();
            return Result.Succeeded;
        }

        private static BitmapImage? LoadImage(string path)
        {
            if (!File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
