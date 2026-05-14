// ================================================================
// DwgExportService.cs  —  E3G BIM Tools  FASE 3
// Exportació de la Drafting View a fitxer DWG
//
// Usa Autodesk.Revit.DB.DWGExportOptions per configurar:
//   - Versió d'AutoCAD (R2013, R2010, etc.)
//   - Mapeig de capes via fitxer .txt extern (format Revit estàndard)
//   - Colors i unitats
//
// NOTES D'ÚS:
//   - Cridar FORA de qualsevol Transaction activa (R5)
//   - La carpeta de destí es crea automàticament si no existeix
//   - El nom del fitxer inclou timestamp per evitar col·lisions
//   - Per a PDF usar la funció ExportToPdf (requereix Revit 2022+)
//
// API Revit verificada per a Revit 2021–2025:
//   - DWGExportOptions.FileVersion  → ACADVersion enum
//   - DWGExportOptions.Colors       → DWGColor.IndexColors
//   - DWGExportOptions.TargetUnit   → ExportUnit.Millimeter
//   - DWGExportOptions.LayerMapping → path a fitxer .txt de capes
//   - Document.Export(folder, filename, viewIds, opts)
//
// Autors: E3G — Enginyeria i Energia
// Data:   Maig 2026
// ================================================================

using Autodesk.Revit.DB;
using E3G.Saneamiento;       // XarxaInfo
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace E3G.Saneamiento.Schematic
{
    public static class DwgExportService
    {
        // --------------------------------------------------------
        // ENTRY POINT PRINCIPAL
        // --------------------------------------------------------

        /// <summary>
        /// Exporta la vista a un fitxer .dwg.
        /// Ha de cridar-se FORA de qualsevol Transaction activa [R5].
        /// Retorna el path complet del fitxer generat.
        /// </summary>
        public static string Export(
            Document            doc,
            View                view,
            XarxaInfo           xarxa,
            SchematicParameters prm)
        {
            // ---- Preparar carpeta ----
            string folder = prm.Export.OutputFolder;
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            // ---- Nom del fitxer (sense extensió: Revit l'afegeix) ----
            string sistema   = SanitizeFileName(xarxa?.Sistema ?? "Sistema");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string baseName  = $"{prm.Export.FilePrefix}_{sistema}_{timestamp}";

            // ---- Fitxer de mapeig de capes temporal ----
            string layerMapPath = WriteLayerMappingFile(prm, folder);

            // ---- Configurar opcions DWG ----
            var opts = new DWGExportOptions
            {
                FileVersion            = prm.Export.DwgVersion,
                Colors                 = ExportColorMode.IndexColors,
                TargetUnit             = ExportUnit.Millimeter,
                ExportingAreas         = false,
                HideUnreferenceViewTags = true,
                HideScopeBox           = true,
                HideReferencePlane     = true,
                PreserveCoincidentLines = false,
            };

            // Assignar fitxer de mapeig si existeix
            if (!string.IsNullOrEmpty(layerMapPath) && File.Exists(layerMapPath))
                opts.LayerMapping = layerMapPath;

            // ---- Col·lecció d'ElementIds de vista ----
            var viewIds = new List<ElementId> { view.Id };

            // ---- Exportar (Revit afegeix .dwg automàticament) ----
            doc.Export(folder, baseName, viewIds, opts);

            // Netejar fitxer de mapeig temporal
            TryDelete(layerMapPath);

            // Revit pot generar "baseName.dwg" o "baseName - Vista.dwg"
            string fullPath = Path.Combine(folder, baseName + ".dwg");
            return File.Exists(fullPath) ? fullPath : folder;
        }

        // --------------------------------------------------------
        // MAPEIG DE CAPES — fitxer .txt format Revit
        // --------------------------------------------------------
        //
        // Format del fitxer de mapeig de capes Revit (.txt):
        //   [LINE WEIGHTS]   (opcional)
        //   [CATEGORIES]
        //   CategoryName  LayerName  Color  LineWeight  LineType
        //
        // Exemple:
        //   Lines - E3G_DN160  E3G-SANEJ-DN160  1  7  Continuous
        //   Text Notes  E3G-TEXT  7  1  Continuous

        private static string WriteLayerMappingFile(SchematicParameters prm, string folder)
        {
            try
            {
                string path = Path.Combine(folder, "E3G_LayerMapping_temp.txt");
                var sb = new StringBuilder();

                sb.AppendLine("[CATEGORIES]");
                sb.AppendLine("# Format: CategoryFullName<TAB>LayerName<TAB>ColorIndex<TAB>LineWeight<TAB>LineType");
                sb.AppendLine("# Capes E3G per DN (sanejament)");

                foreach (var kvp in prm.Export.LayerMap)
                {
                    // El nom de la categoria Revit per a DetailCurve amb LineStyle propi és:
                    // "Lines" per al generic, o el nom de la subcategoria directament
                    string revitCat = $"Lines - E3G_{kvp.Key}";
                    string layer    = kvp.Value.LayerName;
                    int    color    = kvp.Value.AcadColor;
                    sb.AppendLine($"{revitCat}\t{layer}\t{color}\t1\tContinuous");
                }

                // Categories Revit estàndard
                sb.AppendLine($"Lines\t\t\t\tE3G-LINES\t7\t1\tContinuous");
                sb.AppendLine($"Text Notes\t\t\tE3G-TEXT\t7\t1\tContinuous");
                sb.AppendLine($"Dimensions\t\t\tE3G-COTES\t7\t1\tContinuous");

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch
            {
                return null;
            }
        }

        // --------------------------------------------------------
        // EXPORTACIÓ BATCH — totes les vistes E3G
        // --------------------------------------------------------

        /// <summary>
        /// Exporta totes les Drafting Views amb prefix "E3G" al nom.
        /// Útil per a lliurar tota la documentació del projecte.
        /// </summary>
        public static List<string> ExportAllE3GViews(
            Document doc, SchematicParameters prm)
        {
            var paths = new List<string>();

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .Where(v => v.Name.StartsWith("E3G", StringComparison.OrdinalIgnoreCase))
                .ToList();

            string folder = prm.Export.OutputFolder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string layerMapPath = WriteLayerMappingFile(prm, folder);

            foreach (var view in views)
            {
                try
                {
                    string baseName = SanitizeFileName(view.Name);
                    var opts = new DWGExportOptions
                    {
                        FileVersion    = prm.Export.DwgVersion,
                        Colors         = ExportColorMode.IndexColors,
                        TargetUnit     = ExportUnit.Millimeter,
                        ExportingAreas = false,
                    };
                    if (!string.IsNullOrEmpty(layerMapPath) && File.Exists(layerMapPath))
                        opts.LayerMapping = layerMapPath;

                    doc.Export(folder, baseName, new List<ElementId> { view.Id }, opts);
                    string fp = Path.Combine(folder, baseName + ".dwg");
                    if (File.Exists(fp)) paths.Add(fp);
                }
                catch { /* continuar amb la resta */ }
            }

            TryDelete(layerMapPath);
            return paths;
        }

        // --------------------------------------------------------
        // EXPORTACIÓ A PDF (Revit 2022+)
        // --------------------------------------------------------

        /// <summary>
        /// Exporta la vista a PDF. Requereix Revit 2022 o superior.
        /// En Revit 2021 o anterior llança NotSupportedException.
        /// </summary>
        public static string ExportToPdf(
            Document doc, View view,
            SchematicParameters prm, XarxaInfo xarxa)
        {
            // Comprovem en temps d'execució si PDFExportOptions està disponible
            var pdfType = Type.GetType(
                "Autodesk.Revit.DB.PDFExportOptions, RevitAPI");
            if (pdfType == null)
                throw new NotSupportedException(
                    "L'exportació a PDF requereix Revit 2022 o superior.");

            string folder = prm.Export.OutputFolder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string sistema  = SanitizeFileName(xarxa?.Sistema ?? "Sistema");
            string baseName = $"{prm.Export.FilePrefix}_{sistema}_{DateTime.Now:yyyyMMdd_HHmm}";

            // Crida via reflexió per evitar error de compilació en Revit <2022
            try
            {
                dynamic opts = Activator.CreateInstance(pdfType);
                opts.FileName = baseName;
                opts.Combine  = false;

                var exportMethod = doc.GetType().GetMethod("Export",
                    new[] { typeof(string), typeof(IList<ElementId>), pdfType });
                exportMethod?.Invoke(doc, new object[]
                    { folder, new List<ElementId> { view.Id }, opts });

                return Path.Combine(folder, baseName + ".pdf");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error exportant a PDF: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // --------------------------------------------------------
        // HELPERS
        // --------------------------------------------------------

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_').Replace('·', '-').Replace('—', '-');
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
