using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Models.TableGen;

namespace E3G.BimTools.DiTools.Core.Services.TableGen;

/// <summary>
/// Lee un rango de un archivo .xlsx con ClosedXML preservando:
/// valor formateado, fuente (nombre, tamaño, bold, italic), colores
/// (texto y fondo), bordes y celdas combinadas (Sprint 8 — TableGen).
/// </summary>
internal sealed class ExcelTableReader
{
    // ── Constantes de conversion ─────────────────────────────────────────
    //  Excel guarda el ancho de columna en "character units" (1 cu ≈ 7 px
    //  a 96 dpi, ≈ 2.46 mm para Calibri 11pt).  Usamos 2.46 como factor
    //  aproximado; el usuario puede ajustar la escala en el request.
    private const double ExcelColWidthToMm = 2.46;

    //  Excel guarda la altura de fila en puntos (1 pt = 0.353 mm).
    private const double ExcelRowHeightPtToMm = 0.353;

    // ────────────────────────────────────────────────────────────────────

    public IReadOnlyList<string> GetSheetNames(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        return wb.Worksheets.Select(ws => ws.Name).ToList();
    }

    public string GetUsedRangeAddress(string filePath, string sheetName)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(sheetName);
        var used = ws.RangeUsed();
        return used is null ? "A1:A1" : used.RangeAddress.ToStringRelative(false);
    }

    /// <summary>
    /// Lee el rango indicado (o el rango utilizado si RangeAddress es null)
    /// y devuelve el modelo de datos de la hoja.
    /// </summary>
    public ExcelSheetData Read(TableGenRequest request)
    {
        using var wb = new XLWorkbook(request.FilePath);
        var ws = wb.Worksheet(request.SheetName);

        IXLRange range = string.IsNullOrWhiteSpace(request.RangeAddress)
            ? ws.RangeUsed() ?? ws.Range("A1:A1")
            : ws.Range(request.RangeAddress);

        int firstRow = range.FirstRow().RowNumber();
        int firstCol = range.FirstColumn().ColumnNumber();
        int lastRow  = range.LastRow().RowNumber();
        int lastCol  = range.LastColumn().ColumnNumber();

        int rowCount = lastRow  - firstRow + 1;
        int colCount = lastCol  - firstCol + 1;

        // ── Anchos y alturas ─────────────────────────────────────────────
        var colWidths  = new Dictionary<int, double>(colCount);
        var rowHeights = new Dictionary<int, double>(rowCount);

        for (int c = firstCol; c <= lastCol; c++)
        {
            double w = ws.Column(c).Width * ExcelColWidthToMm;
            colWidths[c - firstCol + 1] = Math.Max(w, 5.0);
        }

        for (int r = firstRow; r <= lastRow; r++)
        {
            double h = ws.Row(r).Height * ExcelRowHeightPtToMm;
            rowHeights[r - firstRow + 1] = Math.Max(h, 3.0);
        }

        // ── Celdas combinadas (pre-index) ────────────────────────────────
        // Construimos un diccionario (absRow, absCol) → MergedArea para
        // busquedas O(1) al iterar celdas.
        var mergeMap = BuildMergeMap(ws.MergedRanges, firstRow, firstCol);

        // ── Lectura de celdas ────────────────────────────────────────────
        var cells = new List<ExcelCellData>(rowCount * colCount);

        for (int r = firstRow; r <= lastRow; r++)
        {
            for (int c = firstCol; c <= lastCol; c++)
            {
                int relRow = r - firstRow + 1;
                int relCol = c - firstCol + 1;

                // Si pertenece a un merge pero no es la celda de inicio, la saltamos.
                if (mergeMap.TryGetValue((r, c), out var ma) && !ma.IsStart(relRow, relCol))
                    continue;

                var xlCell = ws.Cell(r, c);
                cells.Add(ReadCell(xlCell, relRow, relCol, ma, colWidths, rowHeights));
            }
        }

        return new ExcelSheetData
        {
            FilePath      = request.FilePath,
            SheetName     = request.SheetName,
            RangeAddress  = range.RangeAddress.ToStringRelative(false),
            RowCount      = rowCount,
            ColumnCount   = colCount,
            Cells         = cells,
            ColumnWidthsMm = colWidths,
            RowHeightsMm  = rowHeights,
        };
    }

    // ── Helpers privados ─────────────────────────────────────────────────

    private static Dictionary<(int, int), MergedArea> BuildMergeMap(
        IXLRanges mergedRanges, int firstRow, int firstCol)
    {
        var map = new Dictionary<(int, int), MergedArea>();

        foreach (var mr in mergedRanges)
        {
            int mrFirstRow = mr.FirstRow().RowNumber();
            int mrFirstCol = mr.FirstColumn().ColumnNumber();
            int mrLastRow  = mr.LastRow().RowNumber();
            int mrLastCol  = mr.LastColumn().ColumnNumber();

            var area = new MergedArea(
                mrFirstRow - firstRow + 1,
                mrFirstCol - firstCol + 1,
                mrLastRow  - firstRow + 1,
                mrLastCol  - firstCol + 1);

            for (int r = mrFirstRow; r <= mrLastRow; r++)
                for (int c = mrFirstCol; c <= mrLastCol; c++)
                    map[(r, c)] = area;
        }

        return map;
    }

    private static ExcelCellData ReadCell(
        IXLCell cell,
        int relRow, int relCol,
        MergedArea? merge,
        IReadOnlyDictionary<int, double> colWidths,
        IReadOnlyDictionary<int, double> rowHeights)
    {
        var style  = cell.Style;
        var font   = style.Font;
        var fill   = style.Fill;
        var border = style.Border;

        // ── Fuente ───────────────────────────────────────────────────────
        double fontSize = font.FontSize > 0 ? font.FontSize : 10.0;

        // ── Colores ──────────────────────────────────────────────────────
        uint fontColor = XLColorToArgb(font.FontColor, 0xFF000000u);

        // XLFillPatternValues.None = sin relleno (no existe "NoFill" en 0.102.x)
        bool hasBg    = fill.PatternType != XLFillPatternValues.None;
        uint bgColor  = hasBg ? XLColorToArgb(fill.BackgroundColor, 0xFFFFFFFFu) : 0xFFFFFFFFu;
        // Un fondo blanco puro no merece FilledRegion.
        if (bgColor == 0xFFFFFFFFu) hasBg = false;

        // ── Dimensiones (merge expande la celda) ─────────────────────────
        double widthMm  = SumRange(colWidths, relCol,
                                   merge?.ColumnSpan ?? 1, 25.4);
        double heightMm = SumRange(rowHeights, relRow,
                                   merge?.RowSpan    ?? 1, 5.0);

        return new ExcelCellData
        {
            Row    = relRow,
            Column = relCol,
            Value  = cell.IsEmpty() ? null : cell.GetFormattedString(),

            MergedArea = merge,

            FontName   = string.IsNullOrWhiteSpace(font.FontName) ? "Arial" : font.FontName,
            FontSizePt = fontSize,
            Bold       = font.Bold,
            Italic     = font.Italic,

            FontColor       = fontColor,
            BackgroundColor = bgColor,
            HasBackground   = hasBg,

            TopBorder    = MapBorder(border.TopBorder),
            BottomBorder = MapBorder(border.BottomBorder),
            LeftBorder   = MapBorder(border.LeftBorder),
            RightBorder  = MapBorder(border.RightBorder),

            WidthMm  = widthMm,
            HeightMm = heightMm,
        };
    }

    private static double SumRange(
        IReadOnlyDictionary<int, double> dict, int start, int count, double defaultVal)
    {
        double total = 0;
        for (int i = start; i < start + count; i++)
            total += dict.TryGetValue(i, out var v) ? v : defaultVal;
        return total;
    }

    private static uint XLColorToArgb(XLColor color, uint fallback)
    {
        // XLColorType en 0.102.x: Color | Theme | Indexed  (no existe "Auto")
        if (color is null || color.ColorType != XLColorType.Color)
            return fallback;

        var c = color.Color;
        return (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);
    }

    private static CellBorderStyle MapBorder(XLBorderStyleValues style) => style switch
    {
        XLBorderStyleValues.None         => CellBorderStyle.None,
        XLBorderStyleValues.Hair         => CellBorderStyle.Thin,
        XLBorderStyleValues.Thin         => CellBorderStyle.Thin,
        XLBorderStyleValues.DashDot      => CellBorderStyle.Thin,
        XLBorderStyleValues.DashDotDot   => CellBorderStyle.Thin,
        XLBorderStyleValues.Dashed       => CellBorderStyle.Thin,
        XLBorderStyleValues.Dotted       => CellBorderStyle.Thin,
        XLBorderStyleValues.MediumDashDot    => CellBorderStyle.Medium,
        XLBorderStyleValues.MediumDashDotDot => CellBorderStyle.Medium,
        XLBorderStyleValues.MediumDashed     => CellBorderStyle.Medium,
        XLBorderStyleValues.Medium       => CellBorderStyle.Medium,
        XLBorderStyleValues.SlantDashDot => CellBorderStyle.Medium,
        XLBorderStyleValues.Thick        => CellBorderStyle.Thick,
        XLBorderStyleValues.Double       => CellBorderStyle.Double,
        _                                => CellBorderStyle.None,
    };
}
