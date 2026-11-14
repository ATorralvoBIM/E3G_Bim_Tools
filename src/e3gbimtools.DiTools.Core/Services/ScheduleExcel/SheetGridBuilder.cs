using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Construye las rejillas de vista previa con los mismos colores que el Excel:
/// desde la tabla leida de Revit (antes de exportar), desde el formato
/// editable, o leyendo un .xlsx (al importar).
/// </summary>
public static class SheetGridBuilder
{
    private const string TitleBg = "1F4E78";
    private const string HeadingBg = "D9E1F2";
    private const string Group1Bg = "BDD7EE";
    private const string GroupNBg = "F2F2F2";
    private const string ReadOnlyHeadBg = "D9D9D9";
    private const string ReadOnlyBg = "F2F2F2";
    private const string Muted = "595959";

    // ====================================================================
    // Desde la tabla de Revit («como en Revit»)
    // ====================================================================

    public static SheetGrid FromSnapshot(ScheduleSnapshot snap, int maxRows)
    {
        int nc = Math.Max(1, snap.ColumnCount);
        var grid = new SheetGrid { SheetName = snap.Name, ScheduleName = snap.Name };
        var widths = Enumerable.Range(0, nc)
            .Select(c => Math.Max(12, (c < snap.ColumnWidthsMm.Count ? snap.ColumnWidthsMm[c] : 25.4) / 2.1))
            .ToArray();

        var titles = snap.TitleLines.Where(t => !string.IsNullOrWhiteSpace(t)).DefaultIfEmpty(snap.Name);
        foreach (var t in titles)
            grid.Rows.Add(Single(t, nc, TitleBg, "FFFFFF", bold: true, align: GridAlign.Center));

        foreach (var heading in snap.HeadingRows)
        {
            var row = new GridRow();
            for (int c = 0; c < nc && c < heading.Count; c++)
            {
                var sc = heading[c];
                if (sc.Covered) continue;
                row.Cells.Add(new GridCell
                {
                    Column = c + 1, ColSpan = Math.Max(1, sc.ColSpan), Text = sc.Text,
                    Background = HeadingBg, Bold = true, Align = GridAlign.Center,
                });
                if (sc.ColSpan <= 1) Fit(widths, c, sc.Text, 1.15);
            }
            grid.Rows.Add(row);
        }

        int dataRows = 0;
        foreach (var r in snap.Rows)
        {
            dataRows++;
            if (grid.Rows.Count >= maxRows) continue;
            if (r.Kind == ScheduleRowKind.Group)
            {
                grid.Rows.Add(Single(r.Cells.FirstOrDefault()?.Text ?? "", nc,
                    r.Level <= 1 ? Group1Bg : GroupNBg, null, bold: true, italic: r.Level > 1));
                continue;
            }
            var row = new GridRow();
            for (int c = 0; c < nc && c < r.Cells.Count; c++)
            {
                var sc = r.Cells[c];
                if (sc.Covered) continue;
                bool numeric = ScheduleExcelWriter.TryParseNumber(sc.Text, snap.DecimalSeparator, snap.GroupSeparator, out _, out var dec, out _);
                row.Cells.Add(new GridCell
                {
                    Column = c + 1, ColSpan = Math.Max(1, sc.ColSpan), Text = sc.Text,
                    Background = sc.BackgroundHex, Bold = sc.Bold,
                    Align = numeric ? (dec == 0 ? GridAlign.Center : GridAlign.Right) : GridAlign.Left,
                });
                if (sc.ColSpan <= 1) Fit(widths, c, sc.Text, 1.05);
            }
            grid.Rows.Add(row);
        }

        grid.TotalRows = titles.Count() + snap.HeadingRows.Count + dataRows;
        grid.ColumnWidths.AddRange(widths.Select(w => Math.Min(60, w)));
        return grid;
    }

    // ====================================================================
    // Desde el formato editable (una fila por elemento)
    // ====================================================================

    public static SheetGrid FromEditable(ScheduleEditableData data, int maxRows)
    {
        int nc = data.Columns.Count + 1;
        var grid = new SheetGrid { SheetName = data.Name, ScheduleName = data.Name };
        grid.Rows.Add(Single(data.Name, nc, TitleBg, "FFFFFF", bold: true, align: GridAlign.Center));

        var head = new GridRow();
        head.Cells.Add(new GridCell { Column = 1, Text = ScheduleEditableWorkbook.IdHeading, Background = ReadOnlyHeadBg, Foreground = Muted, Bold = true, Align = GridAlign.Center });
        for (int i = 0; i < data.Columns.Count; i++)
        {
            var col = data.Columns[i];
            head.Cells.Add(new GridCell
            {
                Column = i + 2, Text = col.Heading, Bold = true, Align = GridAlign.Center,
                Background = col.Editable ? HeadingBg : ReadOnlyHeadBg, Foreground = col.Editable ? null : Muted,
            });
        }
        grid.Rows.Add(head);

        var widths = new double[nc];
        widths[0] = 11;
        for (int i = 0; i < data.Columns.Count; i++) widths[i + 1] = Math.Max(10, data.Columns[i].Heading.Length * 1.1 + 2);

        foreach (var r in data.Rows.Take(Math.Max(0, maxRows - 2)))
        {
            var row = new GridRow();
            row.Cells.Add(new GridCell { Column = 1, Text = r.ElementId.ToString(CultureInfo.InvariantCulture), Background = ReadOnlyBg, Foreground = Muted });
            for (int i = 0; i < data.Columns.Count; i++)
            {
                var col = data.Columns[i];
                var v = i < r.Values.Count ? r.Values[i] : null;
                string text = v switch
                {
                    null => "",
                    double d => d.ToString(col.Kind == EditableKind.Number ? "N" + col.Decimals : "0", CultureInfo.CurrentCulture),
                    bool b => b ? "Sí" : "No",
                    _ => Convert.ToString(v, CultureInfo.CurrentCulture) ?? "",
                };
                row.Cells.Add(new GridCell
                {
                    Column = i + 2, Text = text, Background = col.Editable ? null : ReadOnlyBg,
                    Align = v is double ? GridAlign.Right : GridAlign.Left,
                });
                Fit(widths, i + 1, text, 1.05);
            }
            grid.Rows.Add(row);
        }
        grid.TotalRows = data.Rows.Count + 2;
        grid.ColumnWidths.AddRange(widths.Select(w => Math.Min(60, w)));
        return grid;
    }

    // ====================================================================
    // Desde un Excel (al importar): tal y como esta guardado
    // ====================================================================

    public static List<SheetGrid> FromWorkbook(string path, int maxRows)
    {
        using var wb = new XLWorkbook(path);
        var meta = ScheduleWorkbookMeta.Read(wb);
        var result = new List<SheetGrid>();

        foreach (var ws in wb.Worksheets)
        {
            if (ws.Visibility != XLWorksheetVisibility.Visible || ScheduleWorkbookMeta.IsInternal(ws.Name)) continue;
            if (!meta.ContainsKey(ws.Name)) continue;

            var grid = new SheetGrid
            {
                SheetName = ws.Name,
                ScheduleName = meta[ws.Name].TryGetValue("ScheduleName", out var n) ? n : ws.Name,
            };
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++) grid.ColumnWidths.Add(ws.Column(c).Width);

            // Celdas combinadas: la de arriba a la izquierda ocupa el ancho; el resto se omite
            var spans = new Dictionary<(int, int), int>();
            var covered = new HashSet<(int, int)>();
            foreach (var range in ws.MergedRanges)
            {
                int r1 = range.RangeAddress.FirstAddress.RowNumber, c1 = range.RangeAddress.FirstAddress.ColumnNumber;
                int r2 = range.RangeAddress.LastAddress.RowNumber, c2 = range.RangeAddress.LastAddress.ColumnNumber;
                spans[(r1, c1)] = c2 - c1 + 1;
                for (int r = r1; r <= r2; r++)
                    for (int c = c1; c <= c2; c++)
                        if (r != r1 || c != c1) covered.Add((r, c));
            }

            for (int r = 1; r <= lastRow && grid.Rows.Count < maxRows; r++)
            {
                var row = new GridRow { ExcelRow = r };
                for (int c = 1; c <= lastCol; c++)
                {
                    if (covered.Contains((r, c))) continue;
                    var cell = ws.Cell(r, c);
                    var style = cell.Style;
                    string text;
                    try { text = cell.GetFormattedString(); }
                    catch { text = cell.GetString(); }
                    if (cell.DataType == XLDataType.Number) text = ToCurrentCulture(text);
                    row.Cells.Add(new GridCell
                    {
                        Column = c,
                        ColSpan = spans.TryGetValue((r, c), out var s) ? s : 1,
                        Text = text,
                        Background = Hex(style.Fill.PatternType == XLFillPatternValues.None ? null : style.Fill.BackgroundColor),
                        Foreground = Hex(style.Font.FontColor),
                        Bold = style.Font.Bold,
                        Italic = style.Font.Italic,
                        Align = style.Alignment.Horizontal switch
                        {
                            XLAlignmentHorizontalValues.Center => GridAlign.Center,
                            XLAlignmentHorizontalValues.Right => GridAlign.Right,
                            XLAlignmentHorizontalValues.General when cell.DataType == XLDataType.Number => GridAlign.Right,
                            _ => GridAlign.Left,
                        },
                    });
                }
                grid.Rows.Add(row);
            }
            grid.TotalRows = lastRow;
            result.Add(grid);
        }
        return result;
    }

    // ====================================================================

    private static GridRow Single(string text, int span, string? bg, string? fg, bool bold = false, bool italic = false, GridAlign align = GridAlign.Left)
    {
        var row = new GridRow();
        row.Cells.Add(new GridCell { Column = 1, ColSpan = span, Text = text, Background = bg, Foreground = fg, Bold = bold, Italic = italic, Align = align });
        return row;
    }

    private static void Fit(double[] widths, int c, string? text, double k)
    {
        if (string.IsNullOrEmpty(text) || c >= widths.Length) return;
        double w = text.Length * k + 2;
        if (w > widths[c]) widths[c] = w;
    }

    /// <summary>ClosedXML formatea los números con separadores invariantes ("1,234.5"); se pasan a los del equipo.</summary>
    internal static string ToCurrentCulture(string text)
    {
        var nf = CultureInfo.CurrentCulture.NumberFormat;
        if (nf.NumberDecimalSeparator == "." && nf.NumberGroupSeparator == ",") return text;
        return text.Replace(",", "\u0001").Replace(".", nf.NumberDecimalSeparator).Replace("\u0001", nf.NumberGroupSeparator);
    }

    private static string? Hex(XLColor? color)
    {
        try
        {
            if (color is null || color.ColorType != XLColorType.Color) return null;
            var c = color.Color;
            if (c.A == 0) return null;
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }
        catch { return null; }
    }
}
