using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.TableGen;

namespace E3G.BimTools.DiTools.Core.Services.TableGen;

/// <summary>
/// Convierte un <see cref="ViewSchedule"/> de Revit en un
/// <see cref="ExcelSheetData"/> reutilizable por <see cref="DraftingViewGenerator"/>
/// (Sprint 9 — TableGen P2).
///
/// La tabla generada sigue el esquema visual tipico de un schedule de Revit:
///   • Cabecera: fondo azul claro (#BDD7EE), texto negro, negrita, bordes Thin.
///   • Filas de datos: fondo blanco, texto negro, bordes Thin.
///   • Anchos de columna: tomados del schedule (ft → mm), multiplicados
///     por <see cref="ScheduleToViewRequest.ColumnWidthMultiplier"/>.
/// </summary>
internal sealed class ScheduleTableReader
{
    // Colores corporativos del schedule exportado
    private const uint HeaderBgArgb  = 0xFF_BDD7EE; // azul claro Excel
    private const uint HeaderFgArgb  = 0xFF_000000; // negro
    private const uint BodyFgArgb    = 0xFF_000000; // negro
    private const uint BodyBgEvenArgb = 0xFF_FFFFFF; // blanco
    private const uint BodyBgOddArgb  = 0xFF_F2F2F2; // gris muy claro

    private const double DefaultColWidthMm = 30.0;
    private const double FtToMm = 304.8;

    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Construye un <see cref="ExcelSheetData"/> a partir de un schedule.
    /// </summary>
    public ExcelSheetData Read(ViewSchedule schedule, ScheduleToViewRequest req)
    {
        if (schedule is null) throw new ArgumentNullException(nameof(schedule));
        if (req      is null) throw new ArgumentNullException(nameof(req));

        var tableData = schedule.GetTableData();

        // Secciones Header y Body (Footer se omite intencionalmente).
        var headerSection = tableData.GetSectionData(SectionType.Header);
        var bodySection   = tableData.GetSectionData(SectionType.Body);

        int colCount    = bodySection.NumberOfColumns;
        int headerRows  = headerSection.NumberOfRows;
        int bodyRows    = bodySection.NumberOfRows;
        int totalRows   = headerRows + bodyRows;

        // ── Anchos de columna ──────────────────────────────────────────
        var colWidthsMm = new Dictionary<int, double>(colCount);
        for (int c = 0; c < colCount; c++)
        {
            double ft = bodySection.GetColumnWidth(c);
            double mm = (ft > 0 ? ft * FtToMm : DefaultColWidthMm)
                        * req.ColumnWidthMultiplier;
            colWidthsMm[c + 1] = Math.Max(mm, 10.0);
        }

        // ── Alturas de fila ────────────────────────────────────────────
        var rowHeightsMm = new Dictionary<int, double>(totalRows);
        for (int r = 1; r <= headerRows; r++)
            rowHeightsMm[r] = req.HeaderRowHeightMm;
        for (int r = headerRows + 1; r <= totalRows; r++)
            rowHeightsMm[r] = req.BodyRowHeightMm;

        // ── Lectura de celdas ──────────────────────────────────────────
        var cells = new List<ExcelCellData>(totalRows * colCount);

        // Cabecera
        for (int r = 0; r < headerRows; r++)
        {
            for (int c = 0; c < colCount; c++)
            {
                // En la cabecera intentamos detectar celdas combinadas horizontalmente.
                // La API no expone merges directamente, asi que comparamos textos
                // con la celda anterior y asumimos que si la celda esta vacia y la
                // anterior no, puede ser parte de un merge (heuristica simple).
                string text = GetCellText(headerSection, r, c);
                int relRow = r + 1;
                int relCol = c + 1;

                cells.Add(new ExcelCellData
                {
                    Row    = relRow,
                    Column = relCol,
                    Value  = string.IsNullOrEmpty(text) ? null : text,

                    FontName   = "Arial",
                    FontSizePt = 9.0,
                    Bold       = true,
                    Italic     = false,

                    FontColor       = HeaderFgArgb,
                    BackgroundColor = HeaderBgArgb,
                    HasBackground   = true,

                    TopBorder    = relRow == 1 ? CellBorderStyle.Medium : CellBorderStyle.Thin,
                    BottomBorder = CellBorderStyle.Thin,
                    LeftBorder   = relCol == 1 ? CellBorderStyle.Medium : CellBorderStyle.Thin,
                    RightBorder  = relCol == colCount ? CellBorderStyle.Medium : CellBorderStyle.Thin,

                    WidthMm  = colWidthsMm.TryGetValue(relCol, out var w) ? w : DefaultColWidthMm,
                    HeightMm = req.HeaderRowHeightMm,
                });
            }
        }

        // Cuerpo
        for (int r = 0; r < bodyRows; r++)
        {
            bool isEven = (r % 2 == 0);
            uint bgColor = isEven ? BodyBgEvenArgb : BodyBgOddArgb;

            for (int c = 0; c < colCount; c++)
            {
                string text  = GetCellText(bodySection, r, c);
                int relRow   = headerRows + r + 1;
                int relCol   = c + 1;
                bool isLast  = (r == bodyRows - 1);

                cells.Add(new ExcelCellData
                {
                    Row    = relRow,
                    Column = relCol,
                    Value  = string.IsNullOrEmpty(text) ? null : text,

                    FontName   = "Arial",
                    FontSizePt = 8.0,
                    Bold       = false,
                    Italic     = false,

                    FontColor       = BodyFgArgb,
                    BackgroundColor = bgColor,
                    HasBackground   = bgColor != BodyBgEvenArgb, // solo coloreamos filas impares

                    TopBorder    = CellBorderStyle.Thin,
                    BottomBorder = isLast ? CellBorderStyle.Medium : CellBorderStyle.Thin,
                    LeftBorder   = relCol == 1 ? CellBorderStyle.Medium : CellBorderStyle.Thin,
                    RightBorder  = relCol == colCount ? CellBorderStyle.Medium : CellBorderStyle.Thin,

                    WidthMm  = colWidthsMm.TryGetValue(relCol, out var w) ? w : DefaultColWidthMm,
                    HeightMm = req.BodyRowHeightMm,
                });
            }
        }

        return new ExcelSheetData
        {
            FilePath       = schedule.Name,   // se reutiliza el campo como etiqueta
            SheetName      = schedule.Name,
            RangeAddress   = $"A1:{ColLetter(colCount)}{totalRows}",
            RowCount       = totalRows,
            ColumnCount    = colCount,
            Cells          = cells,
            ColumnWidthsMm = colWidthsMm,
            RowHeightsMm   = rowHeightsMm,
        };
    }

    /// <summary>
    /// Devuelve informacion basica de todos los ViewSchedules del documento.
    /// </summary>
    public IReadOnlyList<ScheduleInfo> GetSchedules(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule)
            .OrderBy(s => s.Name)
            .Select(s =>
            {
                int rows = 0;
                try
                {
                    var td = s.GetTableData();
                    rows   = td.GetSectionData(SectionType.Body).NumberOfRows;
                }
                catch { /* si el schedule tiene problemas, lo listamos igual */ }

                return new ScheduleInfo
                {
                    Id       = s.Id,
                    Name     = s.Name,
                    Category = s.Definition?.CategoryId != null
                               ? Category.GetCategory(doc, s.Definition.CategoryId)?.Name ?? ""
                               : "",
                    RowCount = rows,
                };
            })
            .ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string GetCellText(TableSectionData section, int row, int col)
    {
        try { return section.GetCellText(row, col) ?? ""; }
        catch { return ""; }
    }

    private static string ColLetter(int colNumber)
    {
        // Convierte 1→A, 26→Z, 27→AA, etc.
        string result = "";
        while (colNumber > 0)
        {
            colNumber--;
            result = (char)('A' + colNumber % 26) + result;
            colNumber /= 26;
        }
        return result;
    }
}
