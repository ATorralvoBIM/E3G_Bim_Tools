// ============================================================================
//  SymbolFamilyLoader.cs
//  Resuelve un diccionario {Kind → ruta.rfa} en {Kind → FamilySymbol cargado},
//  con validación de categoría (solo Detail Item / Generic Annotation).
//
//  Si la familia ya existe en el proyecto se reutiliza; si no, se carga con
//  Document.LoadFamily.  Cualquier .rfa de categoría inválida se descarta y
//  se registra en el report devuelto.
//
//  Requiere ejecutarse DENTRO de una Transaction abierta por el llamador
//  (LoadFamily modifica el documento).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.RevitBridge.Schematic
{
    public sealed class SymbolFamilyLoader
    {
        public sealed class LoadReport
        {
            public Dictionary<string, FamilySymbol> Resolved { get; }
                = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            public List<string> Errors { get; } = new List<string>();
            public int LoadedCount { get; set; }
            public int ReusedCount { get; set; }
            public int RejectedCount { get; set; }

            public bool HasIssues => Errors.Count > 0;

            public string Summary()
            {
                var sb = new StringBuilder();
                if (LoadedCount > 0)   sb.AppendLine($"Cargadas: {LoadedCount}");
                if (ReusedCount > 0)   sb.AppendLine($"Reutilizadas: {ReusedCount}");
                if (RejectedCount > 0) sb.AppendLine($"Rechazadas: {RejectedCount}");
                foreach (var e in Errors.Take(8)) sb.AppendLine("• " + e);
                if (Errors.Count > 8) sb.AppendLine($"… y {Errors.Count - 8} más");
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// Carga (o reutiliza) cada .rfa, validando categoría.  Llamar dentro
        /// de una Transaction abierta.
        /// </summary>
        public LoadReport LoadAll(Document doc, IReadOnlyDictionary<string, string> kindToPath)
        {
            var report = new LoadReport();
            if (doc == null || kindToPath == null || kindToPath.Count == 0) return report;

            foreach (var kv in kindToPath)
            {
                var kind = kv.Key;
                var path = kv.Value;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    report.RejectedCount++;
                    report.Errors.Add($"{kind}: archivo no encontrado ({path})");
                    continue;
                }

                try
                {
                    var sym = ResolveOrLoad(doc, path, out bool reused);
                    if (sym == null)
                    {
                        report.RejectedCount++;
                        report.Errors.Add($"{kind}: no se pudo cargar el .rfa");
                        continue;
                    }

                    var bic = (BuiltInCategory)sym.Category.Id.Value;
                    bool ok = bic == BuiltInCategory.OST_DetailComponents
                           || bic == BuiltInCategory.OST_GenericAnnotation;
                    if (!ok)
                    {
                        report.RejectedCount++;
                        report.Errors.Add(
                            $"{kind}: categoría '{sym.Category?.Name ?? "?"}' no es " +
                            "Detail Item ni Generic Annotation");
                        continue;
                    }

                    if (!sym.IsActive) sym.Activate();
                    report.Resolved[kind] = sym;
                    if (reused) report.ReusedCount++; else report.LoadedCount++;
                }
                catch (Exception ex)
                {
                    report.RejectedCount++;
                    report.Errors.Add($"{kind}: " + ex.Message);
                }
            }

            return report;
        }

        // ── Internos ──────────────────────────────────────────────────────

        private static FamilySymbol ResolveOrLoad(Document doc, string rfaPath, out bool reused)
        {
            reused = false;
            var famName = Path.GetFileNameWithoutExtension(rfaPath);

            // 1) ¿Ya está cargada?
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => string.Equals(fs.FamilyName, famName,
                    StringComparison.OrdinalIgnoreCase));
            if (existing != null) { reused = true; return existing; }

            // 2) Cargar desde disco.
            if (!doc.LoadFamily(rfaPath, out Family loaded) || loaded == null) return null;
            var firstId = loaded.GetFamilySymbolIds().FirstOrDefault();
            return firstId != null && firstId != ElementId.InvalidElementId
                ? doc.GetElement(firstId) as FamilySymbol
                : null;
        }
    }
}
