// ============================================================================
//  PublishSheetCommand.cs
//  BasePointView — Exportador Revit (Sprint: láminas georreferenciadas).
//
//  «Publicar lámina»: exporta la vista 2D activa (planta, plano de planta…) a
//  PNG y la publica en el backend de BasePointView como lámina georreferenciada.
//  El visor la muestra como un plano horizontal situado en su posición y cota
//  reales sobre el modelo 3D (estilo Dalux).
//
//  La georreferencia se calcula a partir de la región de recorte (Crop Box) de
//  la vista — o, si no está activa, del contorno de los elementos visibles —,
//  convertida a coordenadas del visor con la MISMA transformación que el
//  exportador GLB:  pies → metros (×0,3048) y Z-up → Y-up:  (x, z, −y).
//
//  La URL del backend y la clave de API se leen de la configuración del usuario
//  (%APPDATA%\BasePointView\config.json), nunca del binario.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.BimTools.BasePointExport
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class PublishSheetCommand : IExternalCommand
    {
        private const string Title = "BasePointView — Publicar lámina";
        private const double FeetToMeters = 0.3048;
        private static readonly HttpClient Http = new();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var document = uidoc?.Document;
                if (document == null)
                {
                    message = "No hay documento activo.";
                    return Result.Failed;
                }

                var config = PublishConfig.LoadOrCreate();
                if (string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    TaskDialog.Show(Title,
                        "Configura la URL del backend y la clave de API en:\n\n" +
                        PublishConfig.ConfigPath + "\n\n" +
                        "(se ha creado una plantilla). Después vuelve a publicar.");
                    return Result.Cancelled;
                }

                var view = uidoc!.ActiveView;
                if (view == null || view.IsTemplate || view is View3D)
                {
                    TaskDialog.Show(Title,
                        "Activa una vista 2D (planta o plano de planta) antes de publicar la lámina.");
                    return Result.Cancelled;
                }

                if (!TryComputeGeoref(document, view, out var georef, out var why))
                {
                    TaskDialog.Show(Title,
                        "No se pudo calcular la georreferencia de la vista:\n\n" + why +
                        "\n\nActiva la región de recorte (Crop) en la vista o usa una planta.");
                    return Result.Cancelled;
                }

                var dir = Path.Combine(Path.GetTempPath(), "bpv-sheet-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    var pngPath = ExportImage(document, view, dir, georef.AspectRatio);

                    var project = Fallback(config.Project, DocumentName(document));
                    var name = view.Name;

                    // CDE: si hay ProjectId, subir como item «plan» a una carpeta del
                    // proyecto; si no, publicar como documento plano (clásico).
                    if (!string.IsNullOrWhiteSpace(config.ProjectId))
                    {
                        var folder = Fallback(config.Folder, "Planos");
                        PublishToProject(config, name, folder, pngPath, georef);
                        TaskDialog.Show(Title,
                            "Lámina publicada al proyecto del CDE.\n\n" +
                            "Proyecto (id): " + config.ProjectId + "\n" +
                            "Carpeta: " + folder + "\n" +
                            "Lámina: " + name + "\n\n" +
                            "Ábrela en el visor desde el panel «Proyecto».");
                        return Result.Succeeded;
                    }

                    var id = Publish(config.BackendUrl, config.ApiKey, project, name, pngPath, georef);

                    TaskDialog.Show(Title,
                        "Lámina publicada al visor.\n\n" +
                        "Proyecto: " + project + "\n" +
                        "Lámina: " + name + "\n" +
                        "Id: " + id + "\n\n" +
                        "Ábrela en el visor desde «Planos» → «Cargar láminas publicadas».");
                    return Result.Succeeded;
                }
                finally
                {
                    TryDeleteDir(dir);
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(Title, "Excepción al publicar la lámina:\n\n" + ex);
                return Result.Failed;
            }
        }

        // --- Georreferencia -------------------------------------------------

        /// <summary>Rectángulo de la lámina en coordenadas del visor (metros, Y-up).</summary>
        internal readonly struct Georef
        {
            public double CenterX { get; init; }
            public double CenterZ { get; init; }
            public double Width { get; init; }
            public double Height { get; init; }
            public double Elevation { get; init; }
            public double Rotation { get; init; }

            public double AspectRatio => Height > 1e-6 ? Width / Height : 1.0;

            public string ToJson() =>
                "{" +
                "\"centerX\":" + N(CenterX) + "," +
                "\"centerZ\":" + N(CenterZ) + "," +
                "\"width\":" + N(Width) + "," +
                "\"height\":" + N(Height) + "," +
                "\"elevation\":" + N(Elevation) + "," +
                "\"rotation\":" + N(Rotation) +
                "}";

            private static string N(double v) => v.ToString("R", Inv);
        }

        /// <summary>
        /// Calcula la georreferencia: preferentemente desde la Crop Box (rectángulo
        /// exacto de la vista); si no está activa, desde el contorno de los
        /// elementos visibles. Convierte de pies/Z-up a metros/Y-up.
        /// </summary>
        internal static bool TryComputeGeoref(Document document, View view, out Georef georef, out string why)
        {
            georef = default;
            why = "";

            if (view.CropBoxActive && view.CropBox != null)
            {
                var crop = view.CropBox;
                var t = crop.Transform;
                var min = crop.Min;
                var max = crop.Max;

                var localCenter = (min + max) * 0.5;
                var modelCenter = t.OfPoint(localCenter);

                // Extensión a lo largo de los ejes de la vista (BasisX/BasisY).
                var width = (max.X - min.X) * FeetToMeters;
                var height = (max.Y - min.Y) * FeetToMeters;

                // Giro de la vista respecto al norte del modelo (en el plano XY).
                var bx = t.BasisX;
                var rotation = Math.Atan2(bx.Y, bx.X);

                georef = new Georef
                {
                    CenterX = modelCenter.X * FeetToMeters,
                    CenterZ = -modelCenter.Y * FeetToMeters,
                    Elevation = modelCenter.Z * FeetToMeters,
                    Width = Math.Abs(width),
                    Height = Math.Abs(height),
                    Rotation = rotation,
                };
                return georef.Width > 1e-6 && georef.Height > 1e-6;
            }

            // Sin recorte: contorno de los elementos visibles (alineado a ejes).
            var bb = VisibleBounds(document, view);
            if (bb == null)
            {
                why = "La vista no tiene región de recorte activa ni elementos con contorno.";
                return false;
            }

            var elevation = (view is ViewPlan plan && plan.GenLevel != null)
                ? plan.GenLevel.Elevation
                : bb.Min.Z;

            georef = new Georef
            {
                CenterX = (bb.Min.X + bb.Max.X) * 0.5 * FeetToMeters,
                CenterZ = -(bb.Min.Y + bb.Max.Y) * 0.5 * FeetToMeters,
                Elevation = elevation * FeetToMeters,
                Width = (bb.Max.X - bb.Min.X) * FeetToMeters,
                Height = (bb.Max.Y - bb.Min.Y) * FeetToMeters,
                Rotation = 0.0,
            };
            return georef.Width > 1e-6 && georef.Height > 1e-6;
        }

        /// <summary>Contorno (model coords) de los elementos visibles en la vista.</summary>
        private static BoundingBoxXYZ? VisibleBounds(Document document, View view)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            var any = false;

            foreach (var element in new FilteredElementCollector(document, view.Id)
                         .WhereElementIsNotElementType())
            {
                var bb = element.get_BoundingBox(view);
                if (bb == null) continue;
                any = true;
                minX = Math.Min(minX, bb.Min.X); minY = Math.Min(minY, bb.Min.Y); minZ = Math.Min(minZ, bb.Min.Z);
                maxX = Math.Max(maxX, bb.Max.X); maxY = Math.Max(maxY, bb.Max.Y); maxZ = Math.Max(maxZ, bb.Max.Z);
            }

            if (!any) return null;
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ),
            };
        }

        // --- Exportación de imagen -----------------------------------------

        /// <summary>Exporta la vista a PNG y devuelve la ruta del archivo generado.</summary>
        internal static string ExportImage(Document document, View view, string dir, double aspect)
        {
            // El lado mayor en píxeles; el otro se deriva del aspecto del rectángulo.
            const int maxPixels = 2400;
            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = Path.Combine(dir, "sheet"),
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = maxPixels,
                FitDirection = aspect >= 1.0 ? FitDirectionType.Horizontal : FitDirectionType.Vertical,
            };
            options.SetViewsAndSheets(new List<ElementId> { view.Id });

            document.ExportImage(options);

            var produced = Directory.GetFiles(dir, "*.png")
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .FirstOrDefault();
            if (produced == null)
                throw new Exception("Revit no generó ninguna imagen para la vista.");
            return produced;
        }

        // --- Publicación ----------------------------------------------------

        /// <summary>Sube la lámina (imagen + georef) como item «plan» del proyecto del CDE.</summary>
        private static void PublishToProject(
            PublishConfig config, string name, string folder, string pngPath, Georef georef)
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent("plan"), "kind" },
                { new StringContent(name), "name" },
                { new StringContent(folder), "folder" },
                { new StringContent(georef.ToJson()), "georef" },
            };

            var image = new ByteArrayContent(File.ReadAllBytes(pngPath));
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(image, "image", "sheet.png");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                config.BackendUrl.TrimEnd('/') + "/api/projects/" + config.ProjectId + "/items");
            request.Headers.Add("X-API-Key", config.ApiKey);
            request.Headers.Add("X-User", Environment.UserName);
            request.Content = content;

            using var response = Http.SendAsync(request).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new Exception("El backend respondió " + (int)response.StatusCode + ":\n" + body);
        }

        /// <summary>Sube la imagen + georreferencia al backend. Devuelve el id asignado.</summary>
        private static string Publish(
            string backendUrl, string apiKey, string project, string name,
            string pngPath, Georef georef)
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent(project), "project" },
                { new StringContent(name), "name" },
                { new StringContent(georef.ToJson()), "georef" },
            };

            var image = new ByteArrayContent(File.ReadAllBytes(pngPath));
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(image, "image", "sheet.png");

            using var request = new HttpRequestMessage(
                HttpMethod.Post, backendUrl.TrimEnd('/') + "/api/documents");
            request.Headers.Add("X-API-Key", apiKey);
            request.Content = content;

            using var response = Http.SendAsync(request).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new Exception("El backend respondió " + (int)response.StatusCode + ":\n" + body);

            using var parsed = JsonDocument.Parse(body);
            return parsed.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
        }

        private static string Fallback(string value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static string DocumentName(Document document)
            => string.IsNullOrWhiteSpace(document.Title) ? "Proyecto" : document.Title;

        private static void TryDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { /* carpeta temporal: ignorar */ }
        }
    }
}
