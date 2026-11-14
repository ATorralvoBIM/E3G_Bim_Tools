// ============================================================================
//  ReportExporter.cs
//  RevitBridge — Exporta el resultado de un cálculo MEP a CSV (Excel) o HTML
//  (imprimible a PDF desde el navegador).
//
//  Briefing técnico v1.0 — Sprint 8: "Generación informe PDF/Excel exportable".
//
//  Diseño:
//    - Sin dependencias externas (NuGet). Usa System.IO con encoding UTF-8 BOM
//      para que Excel reconozca los caracteres acentuados al abrir el .csv.
//    - El HTML se genera con estilos inline y CSS @media print, listo para
//      Ctrl+P → "Guardar como PDF" en cualquier navegador.
//    - Acepta una lista de filas (ResultRow se proyecta a un ReportRow plano)
//      más metadatos del cálculo (sistema, criterio, temperatura...).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace E3G.Saneamiento.RevitBridge
{
    /// <summary>
    /// Una fila plana del informe — agnóstica del modelo de la UI para que el
    /// exportador no dependa de tipos internos de la ventana.
    /// </summary>
    public class ReportRow
    {
        public string ElementKind   { get; set; } = "";
        public string BfsIndex      { get; set; } = "";
        public string NodePath      { get; set; } = "";
        public string LevelName     { get; set; } = "";
        public string RevitId       { get; set; } = "";
        public string Size          { get; set; } = "";
        public string RecommendedSize { get; set; } = "";
        public string FixtureCount  { get; set; } = "";
        public string KFactor       { get; set; } = "";
        public string RawFlowLps    { get; set; } = "";
        public string FlowLps       { get; set; } = "";
        public string Velocity      { get; set; } = "";
        public string FrictionPaPm  { get; set; } = "";
        public string TotalLoss     { get; set; } = "";
        public string Status        { get; set; } = "";
        public string StatusKind    { get; set; } = "";   // OK / Warning / Error
        public bool   IsFitting     { get; set; }
    }

    /// <summary>
    /// Cabecera del informe: datos del proyecto y parámetros de cálculo.
    /// </summary>
    public class ReportMetadata
    {
        public string Domain         { get; set; } = "Tuberías";   // o "Conductos"
        public string SystemName     { get; set; } = "";
        public string Criterion      { get; set; } = "";
        public string BuildingType   { get; set; } = "";
        public string FluidLabel     { get; set; } = "Agua";       // "Aire" para conductos
        public string TemperatureC   { get; set; } = "";
        public string SourcePressure { get; set; } = "";
        public string Altitude       { get; set; } = "";           // sólo conductos
        public string ProjectName    { get; set; } = "";
        public string ProjectPath    { get; set; } = "";
        public DateTime GeneratedAt  { get; set; } = DateTime.Now;
        // Totales agregados
        public string TotalSegments  { get; set; } = "";
        public string WorstDpPa      { get; set; } = "";
        public string WarningCount   { get; set; } = "";
        public string ErrorCount     { get; set; } = "";
    }

    /// <summary>
    /// Exporta informes de cálculo a CSV (Excel) o HTML (PDF vía navegador).
    /// </summary>
    public static class ReportExporter
    {
        private static readonly Encoding Utf8WithBom = new UTF8Encoding(true);
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        // ----- API pública -------------------------------------------------

        /// <summary>
        /// Exporta a CSV (Excel-compatible). Separador ';' (estándar locales
        /// europeos donde la coma es separador decimal). BOM UTF-8 para que
        /// Excel detecte los acentos.
        /// </summary>
        public static void WriteCsv(string path, ReportMetadata meta, IList<ReportRow> rows)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(nameof(path));
            if (meta == null) meta = new ReportMetadata();
            if (rows == null) rows = new List<ReportRow>();

            var sb = new StringBuilder();
            // --- Bloque de metadatos como filas etiquetadas ---
            AppendCsvMetadataLine(sb, "Informe de cálculo MEP");
            AppendCsvMetadataLine(sb, "Generado", meta.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Es));
            AppendCsvMetadataLine(sb, "Dominio", meta.Domain);
            AppendCsvMetadataLine(sb, "Sistema MEP", meta.SystemName);
            AppendCsvMetadataLine(sb, "Criterio", meta.Criterion);
            if (!string.IsNullOrEmpty(meta.BuildingType))
                AppendCsvMetadataLine(sb, "Tipo edificio (simult.)", meta.BuildingType);
            AppendCsvMetadataLine(sb, "Fluido", meta.FluidLabel);
            AppendCsvMetadataLine(sb, "Temperatura [°C]", meta.TemperatureC);
            if (!string.IsNullOrEmpty(meta.SourcePressure))
                AppendCsvMetadataLine(sb, "Presión fuente [kPa]", meta.SourcePressure);
            if (!string.IsNullOrEmpty(meta.Altitude))
                AppendCsvMetadataLine(sb, "Altitud [m]", meta.Altitude);
            if (!string.IsNullOrEmpty(meta.ProjectName))
                AppendCsvMetadataLine(sb, "Proyecto", meta.ProjectName);
            AppendCsvMetadataLine(sb, "Tramos totales", meta.TotalSegments);
            AppendCsvMetadataLine(sb, "ΔP peor caso [Pa]", meta.WorstDpPa);
            AppendCsvMetadataLine(sb, "Avisos", meta.WarningCount);
            AppendCsvMetadataLine(sb, "Errores", meta.ErrorCount);
            sb.AppendLine();   // separador

            // --- Cabecera de la tabla ---
            string[] headers = {
                "#", "Elemento", "Nodos", "Nivel", "Element Id",
                "Talla", "DN rec", "N", "K",
                "Q raw [L/s]", "Q dis [L/s]", "v [m/s]",
                "ΔP/m [Pa/m]", "ΔP tot [Pa]", "Estado", "Severidad"
            };
            sb.AppendLine(string.Join(";", headers.Select(EscapeCsv)));

            // --- Datos ---
            foreach (var r in rows)
            {
                string[] cells = {
                    r.BfsIndex, r.ElementKind, r.NodePath, r.LevelName, r.RevitId,
                    r.Size, r.RecommendedSize, r.FixtureCount, r.KFactor,
                    r.RawFlowLps, r.FlowLps, r.Velocity,
                    r.FrictionPaPm, r.TotalLoss, r.Status, r.StatusKind
                };
                sb.AppendLine(string.Join(";", cells.Select(EscapeCsv)));
            }

            File.WriteAllText(path, sb.ToString(), Utf8WithBom);
        }

        /// <summary>
        /// Exporta a HTML imprimible. Estilos inline y @media print listos
        /// para "Guardar como PDF" desde el navegador.
        /// </summary>
        public static void WriteHtml(string path, ReportMetadata meta, IList<ReportRow> rows)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(nameof(path));
            if (meta == null) meta = new ReportMetadata();
            if (rows == null) rows = new List<ReportRow>();

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
            sb.Append("<title>Informe de cálculo MEP — ").Append(EscapeHtml(meta.SystemName)).Append("</title>");
            sb.Append("<style>");
            sb.Append(@"
                * { box-sizing: border-box; }
                body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; color: #222;
                       margin: 24px; }
                h1 { font-size: 16px; margin: 0 0 4px 0; color: #1a3a5e; }
                h2 { font-size: 12px; margin: 16px 0 6px 0; color: #1a3a5e;
                     border-bottom: 1px solid #1a3a5e; padding-bottom: 2px; }
                .meta { display: grid; grid-template-columns: 180px 1fr 180px 1fr;
                        gap: 4px 12px; margin-bottom: 12px; }
                .meta .k { color: #555; font-weight: 600; }
                .meta .v { color: #111; }
                table { border-collapse: collapse; width: 100%; font-size: 10px; }
                thead th { background: #1a3a5e; color: white; text-align: left;
                           padding: 4px 6px; border: 1px solid #1a3a5e; }
                tbody td { padding: 2px 6px; border: 1px solid #d8d8d8; }
                tbody tr.ok    { background: #f1f8f1; }
                tbody tr.warn  { background: #fff7e0; }
                tbody tr.error { background: #fdecec; }
                tbody tr.fit td { color: #555; font-style: italic; }
                .right  { text-align: right; }
                .center { text-align: center; }
                .footer { margin-top: 12px; font-size: 9px; color: #888;
                          display: flex; justify-content: space-between; }
                @media print {
                    body { margin: 12mm; }
                    h1 { font-size: 14px; }
                    table { font-size: 9px; page-break-inside: auto; }
                    thead { display: table-header-group; }
                    tr { page-break-inside: avoid; }
                }
            ");
            sb.Append("</style></head><body>");

            // --- Cabecera ---
            sb.Append("<h1>Informe de cálculo MEP — ").Append(EscapeHtml(meta.Domain)).Append("</h1>");
            sb.Append("<div class=\"meta\">");
            AppendMetaPair(sb, "Sistema MEP", meta.SystemName);
            AppendMetaPair(sb, "Generado", meta.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Es));
            AppendMetaPair(sb, "Criterio dimensionado", meta.Criterion);
            if (!string.IsNullOrEmpty(meta.BuildingType))
                AppendMetaPair(sb, "Tipo edificio (simult.)", meta.BuildingType);
            AppendMetaPair(sb, "Fluido", meta.FluidLabel);
            AppendMetaPair(sb, "Temperatura [°C]", meta.TemperatureC);
            if (!string.IsNullOrEmpty(meta.SourcePressure))
                AppendMetaPair(sb, "Presión fuente [kPa]", meta.SourcePressure);
            if (!string.IsNullOrEmpty(meta.Altitude))
                AppendMetaPair(sb, "Altitud [m]", meta.Altitude);
            if (!string.IsNullOrEmpty(meta.ProjectName))
                AppendMetaPair(sb, "Proyecto", meta.ProjectName);
            AppendMetaPair(sb, "Tramos calculados", meta.TotalSegments);
            AppendMetaPair(sb, "ΔP peor caso [Pa]", meta.WorstDpPa);
            AppendMetaPair(sb, "Avisos / Errores",
                $"{meta.WarningCount} / {meta.ErrorCount}");
            sb.Append("</div>");

            // --- Tabla de resultados ---
            sb.Append("<h2>Resultados por tramo</h2>");
            sb.Append("<table><thead><tr>");
            string[] headers = {
                "#","Elemento","Nodos","Nivel","Element Id","Talla","DN rec","N","K",
                "Q raw [L/s]","Q dis [L/s]","v [m/s]","ΔP/m [Pa/m]","ΔP tot [Pa]","Estado"
            };
            foreach (var h in headers) sb.Append("<th>").Append(EscapeHtml(h)).Append("</th>");
            sb.Append("</tr></thead><tbody>");

            foreach (var r in rows)
            {
                string cssRow = r.IsFitting ? "fit" :
                                r.StatusKind == "OK"      ? "ok" :
                                r.StatusKind == "Warning" ? "warn" :
                                r.StatusKind == "Error"   ? "error" : "";
                sb.Append("<tr class=\"").Append(cssRow).Append("\">");
                AppendTd(sb, r.BfsIndex, "center");
                AppendTd(sb, r.ElementKind, null);
                AppendTd(sb, r.NodePath, null);
                AppendTd(sb, r.LevelName, null);
                AppendTd(sb, r.RevitId, "right");
                AppendTd(sb, r.Size, null);
                AppendTd(sb, r.RecommendedSize, null);
                AppendTd(sb, r.FixtureCount, "right");
                AppendTd(sb, r.KFactor, "right");
                AppendTd(sb, r.RawFlowLps, "right");
                AppendTd(sb, r.FlowLps, "right");
                AppendTd(sb, r.Velocity, "right");
                AppendTd(sb, r.FrictionPaPm, "right");
                AppendTd(sb, r.TotalLoss, "right");
                AppendTd(sb, r.Status, null);
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");

            sb.Append("<div class=\"footer\">");
            sb.Append("<span>E3G BIM Tools — Motor de cálculo MEP</span>");
            sb.Append("<span>").Append(EscapeHtml(meta.ProjectPath ?? "")).Append("</span>");
            sb.Append("<span>").Append(meta.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Es)).Append("</span>");
            sb.Append("</div>");

            sb.Append("</body></html>");
            File.WriteAllText(path, sb.ToString(), Utf8WithBom);
        }

        // ----- Helpers -----------------------------------------------------

        private static void AppendCsvMetadataLine(StringBuilder sb, string k, string v = "")
        {
            sb.Append(EscapeCsv(k)).Append(';').AppendLine(EscapeCsv(v));
        }

        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            bool needsQuotes = s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0;
            if (!needsQuotes) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static void AppendMetaPair(StringBuilder sb, string k, string v)
        {
            sb.Append("<div class=\"k\">").Append(EscapeHtml(k)).Append("</div>");
            sb.Append("<div class=\"v\">").Append(EscapeHtml(v ?? "")).Append("</div>");
        }

        private static void AppendTd(StringBuilder sb, string content, string cssClass)
        {
            if (cssClass != null) sb.Append("<td class=\"").Append(cssClass).Append("\">");
            else sb.Append("<td>");
            sb.Append(EscapeHtml(content ?? ""));
            sb.Append("</td>");
        }

        private static string EscapeHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
