using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Hoja oculta "_Rows" de las pestañas «como en Revit»: para cada fila de
/// datos guarda la fila de Excel, los elementos que la forman y el texto
/// original de cada celda (para detectar que ha cambiado el usuario).
///   Sheet | ExcelRow | Ambiguous | Label | Originals | ElementIds...
/// Los ids van troceados en varias celdas (limite de 32 767 caracteres).
/// </summary>
public static class ScheduleRowsSheet
{
    public const string SheetName = "_Rows";
    private const int IdsPerCell = 2500;

    internal static void Append(XLWorkbook wb, string dataSheet, IEnumerable<(ScheduleRow Row, int ExcelRow)> rows)
    {
        if (!wb.TryGetWorksheet(SheetName, out var ws))
        {
            ws = wb.Worksheets.Add(SheetName);
            var head = new[] { "Sheet", "ExcelRow", "Ambiguous", "Label", "Originals", "ElementIds" };
            for (int i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
            ws.Visibility = XLWorksheetVisibility.Hidden;
        }
        int r = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
        foreach (var (row, excelRow) in rows)
        {
            ws.Cell(r, 1).Value = dataSheet;
            ws.Cell(r, 2).Value = excelRow.ToString(CultureInfo.InvariantCulture);
            ws.Cell(r, 3).Value = row.Ambiguous ? "1" : "0";
            ws.Cell(r, 4).Value = row.Label;
            ws.Cell(r, 5).Value = JsonSerializer.Serialize(row.Cells.Select(c => c.Covered ? "" : c.Text).ToList());
            var ids = row.ElementIds ?? new List<long>();
            for (int chunk = 0; chunk * IdsPerCell < ids.Count; chunk++)
                ws.Cell(r, 6 + chunk).Value = string.Join(",",
                    ids.Skip(chunk * IdsPerCell).Take(IdsPerCell).Select(i => i.ToString(CultureInfo.InvariantCulture)));
            r++;
        }
    }

    /// <summary>Filas registradas por pestaña.</summary>
    internal static Dictionary<string, List<PresentationRow>> Read(XLWorkbook wb)
    {
        var result = new Dictionary<string, List<PresentationRow>>(StringComparer.OrdinalIgnoreCase);
        if (!wb.TryGetWorksheet(SheetName, out var ws)) return result;
        foreach (var xr in ws.RowsUsed().Skip(1))
        {
            var sheet = xr.Cell(1).GetString();
            if (!int.TryParse(xr.Cell(2).GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var excelRow)) continue;
            var row = new PresentationRow
            {
                ExcelRow = excelRow,
                Ambiguous = xr.Cell(3).GetString() == "1",
                Label = xr.Cell(4).GetString(),
            };
            try { row.Originals.AddRange(JsonSerializer.Deserialize<List<string>>(xr.Cell(5).GetString()) ?? new List<string>()); }
            catch (JsonException) { /* sin originales: cada celda con valor se compara con el modelo */ }
            int last = xr.LastCellUsed()?.Address.ColumnNumber ?? 5;
            for (int c = 6; c <= last; c++)
                foreach (var part in xr.Cell(c).GetString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        row.ElementIds.Add(id);

            if (!result.TryGetValue(sheet, out var list)) result[sheet] = list = new List<PresentationRow>();
            list.Add(row);
        }
        return result;
    }
}
