// ============================================================================
//  RvtConverter.cs
//  BasePointView — Conversor de .rvt (modo conversor).
//
//  Abre un .rvt en segundo plano (sin activarlo en la interfaz), lo desliga del
//  central si es colaborativo, y genera todo lo que el visor necesita:
//    · Modelo 3D:  GLB + JSON de propiedades (mismo exportador que «Publicar»)
//                  sobre una vista 3D isométrica nueva, sin plantilla.
//    · Láminas:    un PDF por lámina (exportador PDF nativo de Revit).
//    · Vistas:     plantas como PNG + georreferencia (como «Publicar lámina»).
//    · Tablas:     cada tabla de planificación como JSON (celdas tal cual las
//                  muestra Revit + UniqueIds de los elementos que contiene).
//  El documento se cierra SIN guardar: el .rvt original nunca se modifica.
//
//  Debe ejecutarse en contexto de API (desde el ExternalEvent del servicio).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.BimTools.BasePointExport
{
    /// <summary>Resultado de convertir un .rvt: archivos en disco + manifiesto.</summary>
    public sealed class ConversionResult
    {
        public string? GlbPath { get; set; }
        public string? JsonPath { get; set; }
        public int Elements { get; set; }
        public List<(string Name, string Path, string GeorefJson)> Plans { get; } = new();
        public List<(string Name, string Path)> Sheets { get; } = new();
        public List<(string Name, string Path)> Tables { get; } = new();
        /// <summary>Partes que no se pudieron exportar (no abortan la conversión).</summary>
        public List<string> Warnings { get; } = new();
    }

    public static class RvtConverter
    {
        private const string ViewName = "BasePointView · Conversor";

        /// <summary>
        /// Convierte el .rvt. <paramref name="report"/> recibe el avance aproximado
        /// (0–100) y la fase: abrir 10–30, 3D 30–60, láminas 60–80, vistas 80–90 y
        /// tablas 90–95; la descarga y la subida las informa el servicio.
        /// </summary>
        public static ConversionResult Convert(
            UIApplication uiapp, string rvtPath, string outDir, int maxPlans, Action<int, string> report)
        {
            var app = uiapp.Application;
            report(12, "Abriendo el modelo en Revit");
            CheckVersion(app, rvtPath);

            var options = new OpenOptions { Audit = false };
            var info = BasicFileInfo.Extract(rvtPath);
            if (info.IsWorkshared)
            {
                options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                options.SetOpenWorksetsConfiguration(
                    new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
            }

            var path = ModelPathUtils.ConvertUserVisiblePathToModelPath(rvtPath);
            var openBefore = app.Documents.Cast<Document>().ToList();
            var document = app.OpenDocumentFile(path, options);

            // Si el mismo modelo (o una copia local del mismo central) ya está
            // abierto, Revit devuelve ESE documento en vez de abrir otro. No hay
            // que tocarlo (crearíamos una vista en el modelo del usuario) ni
            // cerrarlo (Revit no deja cerrar el documento activo).
            if (openBefore.Any(d => d.Equals(document)))
            {
                throw new InvalidOperationException(
                    "Este modelo ya está abierto en el Revit del conversor («" + document.Title + "»), " +
                    "y Revit no abre una segunda copia. Ciérralo en ese Revit y pulsa «Volver a convertir».");
            }

            var result = new ConversionResult();
            try
            {
                Directory.CreateDirectory(outDir);
                report(30, "Exportando la geometría 3D");
                Export3D(document, outDir, result);
                ExportSheets(document, outDir, result, report);
                ExportPlans(document, outDir, result, maxPlans, report);
                ExportSchedules(document, outDir, result, report);
            }
            finally
            {
                // Un fallo al cerrar no debe tirar una conversión ya terminada.
                try { document.Close(false); }
                catch (Exception ex) { result.Warnings.Add("No se pudo cerrar el documento: " + ex.Message); }
            }
            return result;
        }

        /// <summary>Informa del paso <paramref name="done"/> de <paramref name="total"/> dentro del tramo [from, to].</summary>
        private static void Step(Action<int, string> report, int from, int to, int done, int total, string label)
            => report(from + (to - from) * done / Math.Max(1, total), $"{label} ({done}/{total})");

        /// <summary>Un .rvt de una versión posterior no se puede abrir: fallar con un mensaje claro.</summary>
        private static void CheckVersion(Autodesk.Revit.ApplicationServices.Application app, string rvtPath)
        {
            var info = BasicFileInfo.Extract(rvtPath);
            if (int.TryParse(info.Format, out var fileYear)
                && int.TryParse(app.VersionNumber, out var appYear)
                && fileYear > appYear)
            {
                throw new InvalidOperationException(
                    $"El archivo es de Revit {fileYear} y el conversor tiene Revit {appYear}. " +
                    $"Hace falta un conversor con Revit {fileYear} o posterior.");
            }
        }

        // --- 3D --------------------------------------------------------------

        private static void Export3D(Document document, string outDir, ConversionResult result)
        {
            View3D view;
            // El documento se cierra sin guardar: crear la vista no toca el original.
            using (var t = new Transaction(document, "BasePointView: vista 3D del conversor"))
            {
                t.Start();
                var type = new FilteredElementCollector(document)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                view = View3D.CreateIsometric(document, type.Id);
                view.ViewTemplateId = ElementId.InvalidElementId;
                view.DetailLevel = ViewDetailLevel.Fine;
                try { view.Name = ViewName; } catch { /* nombre ocupado: se deja el automático */ }
                t.Commit();
            }

            var glb = Path.Combine(outDir, "model.glb");
            var json = Path.Combine(outDir, "model.json");
            var exported = GlbExportRunner.Export(document, view, glb, json);
            result.GlbPath = glb;
            result.JsonPath = json;
            result.Elements = exported.Elements;
        }

        // --- Láminas (PDF) ---------------------------------------------------

        private static void ExportSheets(
            Document document, string outDir, ConversionResult result, Action<int, string> report)
        {
            var sheets = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && !s.IsTemplate && s.CanBePrinted)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dir = Path.Combine(outDir, "sheets");
            Directory.CreateDirectory(dir);
            var index = 0;
            var done = 0;
            foreach (var sheet in sheets)
            {
                Step(report, 60, 80, done++, sheets.Count, "Láminas en PDF");
                var name = $"{sheet.SheetNumber} - {sheet.Name}";
                try
                {
                    var file = $"sheet_{index:D4}";
                    var options = new PDFExportOptions
                    {
                        FileName = file,
                        Combine = true,
                        HideCropBoundaries = true,
                        HideScopeBoxes = true,
                        HideReferencePlane = true,
                        HideUnreferencedViewTags = true,
                        ZoomType = ZoomType.Zoom,
                        ZoomPercentage = 100,
                    };
                    document.Export(dir, new List<ElementId> { sheet.Id }, options);
                    var pdf = Path.Combine(dir, file + ".pdf");
                    if (!File.Exists(pdf)) throw new Exception("Revit no generó el PDF.");
                    result.Sheets.Add((name, pdf));
                    index++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Lámina «{name}»: {ex.Message}");
                }
            }
        }

        // --- Plantas georreferenciadas (PNG) ---------------------------------

        private static readonly HashSet<ViewType> PlanTypes = new()
        {
            ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan, ViewType.AreaPlan,
        };

        private static void ExportPlans(
            Document document, string outDir, ConversionResult result, int maxPlans, Action<int, string> report)
        {
            var plans = new FilteredElementCollector(document)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.CanBePrinted && PlanTypes.Contains(v.ViewType))
                // Primero las que están en láminas (las «oficiales»), luego por cota.
                .OrderBy(v => IsOnSheet(document, v) ? 0 : 1)
                .ThenBy(v => v.GenLevel?.Elevation ?? 0)
                .Take(Math.Max(0, maxPlans))
                .ToList();

            var index = 0;
            var done = 0;
            foreach (var view in plans)
            {
                Step(report, 80, 90, done++, plans.Count, "Vistas de planta");
                try
                {
                    if (!PublishSheetCommand.TryComputeGeoref(document, view, out var georef, out var why))
                    {
                        result.Warnings.Add($"Vista «{view.Name}»: {why}");
                        continue;
                    }
                    // Una carpeta por vista: ExportImage nombra el archivo a su manera.
                    var dir = Path.Combine(outDir, "plans", index.ToString("D4"));
                    Directory.CreateDirectory(dir);
                    var png = PublishSheetCommand.ExportImage(document, view, dir, georef.AspectRatio);
                    result.Plans.Add((view.Name, png, georef.ToJson()));
                    index++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Vista «{view.Name}»: {ex.Message}");
                }
            }
        }

        private static bool IsOnSheet(Document document, View view)
        {
            var p = view.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NUMBER);
            return p != null && !string.IsNullOrWhiteSpace(p.AsString());
        }

        // --- Tablas de planificación (JSON) ----------------------------------

        private static void ExportSchedules(
            Document document, string outDir, ConversionResult result, Action<int, string> report)
        {
            var schedules = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dir = Path.Combine(outDir, "tables");
            Directory.CreateDirectory(dir);
            var index = 0;
            var done = 0;
            foreach (var schedule in schedules)
            {
                Step(report, 90, 95, done++, schedules.Count, "Tablas de planificación");
                try
                {
                    var file = Path.Combine(dir, $"table_{index:D4}.json");
                    File.WriteAllText(file, JsonSerializer.Serialize(ReadSchedule(document, schedule)));
                    result.Tables.Add((schedule.Name, file));
                    index++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Tabla «{schedule.Name}»: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Celdas del cuerpo de la tabla tal cual las muestra Revit (incluidas las
        /// filas de cabecera de columnas, de grupo y de totales). Revit no expone
        /// qué elemento corresponde a cada fila, así que se adjunta la lista de
        /// elementos de la tabla para poder resaltarlos en 3D.
        /// </summary>
        private static object ReadSchedule(Document document, ViewSchedule schedule)
        {
            var table = schedule.GetTableData();
            var body = table.GetSectionData(SectionType.Body);
            var rows = new List<string[]>();
            for (var r = body.FirstRowNumber; r <= body.LastRowNumber; r++)
            {
                var cells = new string[body.LastColumnNumber - body.FirstColumnNumber + 1];
                for (var c = body.FirstColumnNumber; c <= body.LastColumnNumber; c++)
                    cells[c - body.FirstColumnNumber] = schedule.GetCellText(SectionType.Body, r, c) ?? "";
                rows.Add(cells);
            }

            var title = "";
            var header = table.GetSectionData(SectionType.Header);
            if (header != null && header.NumberOfRows > 0 && header.NumberOfColumns > 0)
                title = schedule.GetCellText(SectionType.Header, header.FirstRowNumber, header.FirstColumnNumber) ?? "";

            var definition = schedule.Definition;
            var elementIds = new FilteredElementCollector(document, schedule.Id)
                .WhereElementIsNotElementType()
                .Select(e => e.UniqueId)
                .ToList();

            return new
            {
                name = schedule.Name,
                title,
                // Fila de cabecera de columnas al principio del cuerpo (las
                // cabeceras agrupadas añaden filas que el visor muestra como datos).
                headerRows = definition.ShowHeaders ? 1 : 0,
                rows,
                elementIds,
            };
        }
    }
}
