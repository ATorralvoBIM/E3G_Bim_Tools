using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Escribe un <see cref="ScheduleSnapshot"/> en un .xlsx con el formato
/// corporativo E3G (el de las exportaciones manuales de mediciones):
///   • Titulo: fila combinada, azul oscuro #1F4E78, blanco, negrita 13.
///   • Encabezados: azul claro #D9E1F2, negrita, centrados, borde fino.
///   • Grupo nivel 1: negrita sobre #BDD7EE; niveles inferiores: negrita
///     cursiva sobre #F2F2F2. Combinados en todo el ancho.
///   • Datos: borde fino; los numeros se escriben como numero (con la unidad
///     en el formato de celda) y conservan el fondo de color de Revit.
///   • Paneles inmovilizados bajo los encabezados y hoja "_Meta" oculta.
/// No depende de la API de Revit.
/// </summary>
public static class ScheduleExcelWriter
{
    public const string MetaSheetName = ScheduleWorkbookMeta.SheetName;
    public const string ModePresentation = "Presentation";

    private static readonly XLColor TitleBg   = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor HeadingBg = XLColor.FromHtml("#D9E1F2");
    private static readonly XLColor Group1Bg  = XLColor.FromHtml("#BDD7EE");
    private static readonly XLColor GroupNBg  = XLColor.FromHtml("#F2F2F2");

    /// <summary>Escribe una tabla en <paramref name="filePath"/> y devuelve las filas de cuerpo escritas.</summary>
    public static int Write(ScheduleSnapshot snap, string filePath)
    {
        using var wb = new XLWorkbook();
        int rows = WriteSheet(wb, snap);
        wb.SaveAs(filePath);
        return rows;
    }

    /// <summary>Añade la tabla como una pestaña nueva del libro. Devuelve las filas de cuerpo escritas.</summary>
    public static int WriteSheet(XLWorkbook wb, ScheduleSnapshot snap)
    {
        if (wb is null) throw new ArgumentNullException(nameof(wb));
        if (snap is null) throw new ArgumentNullException(nameof(snap));

        int nc = Math.Max(1, snap.ColumnCount);
        var ws = wb.Worksheets.Add(ScheduleWorkbookMeta.UniqueSheetName(wb, snap.Name));

        var widths = new double[nc];
        for (int c = 0; c < nc; c++)
        {
            double mm = c < snap.ColumnWidthsMm.Count ? snap.ColumnWidthsMm[c] : 25.4;
            widths[c] = Math.Max(12, mm / 2.1);
        }

        int r = 1;

        // ── Titulo ─────────────────────────────────────────────────────
        var titles = snap.TitleLines.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (titles.Count == 0) titles.Add(snap.Name);
        foreach (var t in titles)
        {
            var cell = ws.Cell(r, 1);
            cell.Value = t;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 13;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = TitleBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (nc > 1) ws.Range(r, 1, r, nc).Merge();
            r++;
        }
        int titleLen = titles.Max(t => t.Length);

        // ── Encabezados de columna ─────────────────────────────────────
        int headingTop = r;
        foreach (var row in snap.HeadingRows)
        {
            for (int c = 0; c < nc; c++)
            {
                var cell = ws.Cell(r, c + 1);
                StyleHeading(cell);
                if (c >= row.Count) continue;
                var sc = row[c];
                if (sc.Covered) continue;
                cell.Value = sc.Text;
                if (sc.ColSpan == 1) Fit(widths, c, sc.Text, bold: true);
                int c2 = Math.Min(nc, c + Math.Max(1, sc.ColSpan));
                int r2 = r + Math.Max(1, sc.RowSpan) - 1;
                if (c2 > c + 1 || r2 > r)
                {
                    var range = ws.Range(r, c + 1, r2, c2);
                    range.Merge();
                    range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
            }
            r++;
        }
        int frozenRows = r - 1;
        if (snap.HeadingRows.Count > 0)
            ws.Range(headingTop, 1, r - 1, nc).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        // ── Cuerpo ─────────────────────────────────────────────────────
        int bodyRows = 0;
        var dataRows = new List<(ScheduleRow Row, int ExcelRow)>();
        foreach (var row in snap.Rows)
        {
            if (row.Kind == ScheduleRowKind.Group)
            {
                var text = row.Cells.Select(x => x.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";
                var range = ws.Range(r, 1, r, nc);
                range.Style.Fill.BackgroundColor = row.Level <= 1 ? Group1Bg : GroupNBg;
                range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(r, 1).Value = text;
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Cell(r, 1).Style.Font.Italic = row.Level > 1;
                if (nc > 1) range.Merge();
            }
            else
            {
                dataRows.Add((row, r));
                for (int c = 0; c < nc; c++)
                {
                    var cell = ws.Cell(r, c + 1);
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    if (c >= row.Cells.Count) continue;
                    var sc = row.Cells[c];
                    if (sc.Covered) continue;

                    if (!string.IsNullOrEmpty(sc.BackgroundHex))
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + sc.BackgroundHex);
                    if (sc.Bold) cell.Style.Font.Bold = true;

                    if (TryParseNumber(sc.Text, snap.DecimalSeparator, snap.GroupSeparator,
                            out var value, out var decimals, out var unit))
                    {
                        cell.Value = value;
                        cell.Style.NumberFormat.Format = NumberFormat(decimals, unit, snap.GroupSeparator.HasValue);
                        if (decimals == 0) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                    else if (!string.IsNullOrEmpty(sc.Text))
                    {
                        cell.Value = sc.Text;
                    }

                    if (sc.ColSpan == 1) Fit(widths, c, sc.Text, sc.Bold);
                    if (sc.ColSpan > 1)
                        ws.Range(r, c + 1, r, Math.Min(nc, c + sc.ColSpan)).Merge();
                }
            }
            r++;
            bodyRows++;
        }

        // ── Anchos: que el titulo combinado quepa entero ───────────────
        double total = widths.Sum();
        double needed = titleLen * 1.25 + 2;
        if (total < needed)
        {
            double k = needed / total;
            for (int c = 0; c < nc; c++) widths[c] *= k;
        }
        for (int c = 0; c < nc; c++) ws.Column(c + 1).Width = Math.Round(Math.Min(widths[c], 80), 1);

        if (frozenRows > 0) ws.SheetView.FreezeRows(frozenRows);

        ScheduleWorkbookMeta.Add(wb, ws.Name, new[]
        {
            ("Mode", ModePresentation),
            ("ScheduleName", snap.Name),
            ("ScheduleUniqueId", snap.ScheduleUniqueId ?? ""),
            ("Document", snap.DocumentTitle ?? ""),
            ("DecimalSeparator", snap.DecimalSeparator.ToString()),
            ("GroupSeparator", snap.GroupSeparator?.ToString() ?? ""),
        });

        // Importable: columnas (parametro de cada una) + elementos de cada fila
        if (snap.ColumnFields.Count == nc && dataRows.Any(d => d.Row.ElementIds is not null))
        {
            ScheduleEditableWorkbook.AppendColumns(wb, ws.Name, snap.ColumnFields);
            ScheduleRowsSheet.Append(wb, ws.Name, dataRows.Select(d => (d.Row, d.ExcelRow)));
            if (frozenRows > 0)
                for (int c = 0; c < nc; c++)
                    if (snap.ColumnFields[c].Editable)
                        ws.Cell(frozenRows, c + 1).CreateComment().AddText(
                            "Editable: puedes cambiar los valores de esta columna y reimportar el Excel " +
                            "desde «TableLink». El cambio se aplica a todos los elementos de la fila.");
        }
        return bodyRows;
    }

    // ====================================================================
    // Numeros
    // ====================================================================

    private static readonly Regex NumberRx = new(
        @"^(?<sign>[-−]?)(?<num>\d[\d\s .,']*)\s*(?<unit>[^\d\s.,'\-−][^\d]*)?$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Interpreta un texto formateado por Revit ("382,26", "1.234,5 m", "40", "12 %")
    /// segun los separadores de las unidades del proyecto. Los codigos con ceros a
    /// la izquierda ("007") y los textos ambiguos se dejan como texto.
    /// </summary>
    public static bool TryParseNumber(string? text, char decimalSep, char? groupSep,
        out double value, out int decimals, out string? unit)
    {
        value = 0; decimals = 0; unit = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = NumberRx.Match(text.Trim());
        if (!m.Success) return false;

        string num = m.Groups["num"].Value.TrimEnd();
        string u = m.Groups["unit"].Value.Trim();
        if (u.Length > 0)
        {
            if (u.Length > 6 || u.Any(char.IsWhiteSpace)) return false; // es una frase, no una unidad
            unit = u;
        }

        // Parte entera / decimal
        int dec = num.LastIndexOf(decimalSep);
        string intPart = dec >= 0 ? num[..dec] : num;
        string fracPart = dec >= 0 ? num[(dec + 1)..] : "";
        if (fracPart.Length > 0 && !fracPart.All(char.IsDigit)) return false;
        if (dec >= 0 && fracPart.Length == 0) return false;

        // Separadores de miles: solo el del proyecto y en grupos de 3
        string digits;
        if (intPart.All(char.IsDigit))
        {
            digits = intPart;
        }
        else
        {
            if (groupSep is null) return false;
            var groups = intPart.Replace(' ', ' ').Split(groupSep.Value);
            if (groups.Length < 2 || groups[0].Length is 0 or > 3) return false;
            if (groups.Any(g => !g.All(char.IsDigit))) return false;
            if (groups.Skip(1).Any(g => g.Length != 3)) return false;
            digits = string.Concat(groups);
        }

        if (digits.Length > 1 && digits[0] == '0') return false; // codigo tipo "007"
        if (digits.Length > 15) return false;                  // referencias largas

        var s = digits + (fracPart.Length > 0 ? "." + fracPart : "");
        if (!double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
            return false;
        if (m.Groups["sign"].Value.Length > 0) value = -value;
        decimals = fracPart.Length;
        return true;
    }

    private static string NumberFormat(int decimals, string? unit, bool grouping)
    {
        string f = grouping ? "#,##0" : "0";
        if (decimals > 0) f += "." + new string('0', decimals);
        if (!string.IsNullOrEmpty(unit)) f += " \"" + unit.Replace("\"", "") + "\"";
        return f;
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static void StyleHeading(IXLCell cell)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = HeadingBg;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    private static void Fit(double[] widths, int c, string? text, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return;
        double w = text.Length * (bold ? 1.15 : 1.05) + 2;
        if (w > widths[c]) widths[c] = w;
    }

    /// <summary>Nombre de hoja valido para Excel (max 31, sin : \ / ? * [ ]).</summary>
    public static string SafeSheetName(string? name)
    {
        using var wb = new XLWorkbook();
        return ScheduleWorkbookMeta.UniqueSheetName(wb, name);
    }
}
