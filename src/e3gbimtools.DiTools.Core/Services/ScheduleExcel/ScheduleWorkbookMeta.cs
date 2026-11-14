using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Hoja oculta "_Meta" de los libros de «TableLink»: una fila por dato y
/// pestaña (Sheet | Key | Value), para que un mismo libro pueda llevar varias
/// tablas y reimportarse entero.
/// </summary>
public static class ScheduleWorkbookMeta
{
    public const string SheetName = "_Meta";
    public const string FormatVersion = "ScheduleExcel/2";

    /// <summary>Hojas internas que nunca son tablas.</summary>
    public static bool IsInternal(string sheetName) =>
        sheetName.Equals(SheetName, StringComparison.OrdinalIgnoreCase) ||
        sheetName.Equals(ScheduleEditableWorkbook.ColumnsSheetName, StringComparison.OrdinalIgnoreCase) ||
        sheetName.Equals(ScheduleRowsSheet.SheetName, StringComparison.OrdinalIgnoreCase);

    public static void Add(XLWorkbook wb, string dataSheet, IEnumerable<(string Key, string Value)> values)
    {
        if (!wb.TryGetWorksheet(SheetName, out var meta))
        {
            meta = wb.Worksheets.Add(SheetName);
            meta.Cell(1, 1).Value = "Sheet";
            meta.Cell(1, 2).Value = "Key";
            meta.Cell(1, 3).Value = "Value";
            meta.Visibility = XLWorksheetVisibility.Hidden;
        }
        int r = (meta.LastRowUsed()?.RowNumber() ?? 1) + 1;

        var all = new List<(string, string)>
        {
            ("Format", FormatVersion),
            ("ExportedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("Tool", "E3GBIMTools DiTools " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "")),
        };
        all.AddRange(values);
        foreach (var (k, v) in all)
        {
            meta.Cell(r, 1).Value = dataSheet;
            meta.Cell(r, 2).Value = k;
            meta.Cell(r, 3).Value = v ?? "";
            r++;
        }
    }

    /// <summary>Metadatos por pestaña de datos.</summary>
    public static Dictionary<string, Dictionary<string, string>> Read(XLWorkbook wb)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!wb.TryGetWorksheet(SheetName, out var meta)) return result;
        foreach (var row in meta.RowsUsed().Skip(1))
        {
            var sheet = row.Cell(1).GetString();
            var key = row.Cell(2).GetString().Trim();
            if (sheet.Length == 0 || key.Length == 0) continue;
            if (!result.TryGetValue(sheet, out var d))
                result[sheet] = d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            d[key] = row.Cell(3).GetString();
        }
        return result;
    }

    /// <summary>Nombre de pestaña valido (max 31, sin : \ / ? * [ ]) y unico en el libro.</summary>
    public static string UniqueSheetName(XLWorkbook wb, string? name)
    {
        var s = new string((name ?? "").Select(ch => ":\\/?*[]".IndexOf(ch) >= 0 ? '_' : ch).ToArray()).Trim('\'').Trim();
        if (s.Length == 0 || IsInternal(s)) s = "Tabla";
        string Trunc(string x, int max) => x.Length > max ? x[..max] : x;

        var candidate = Trunc(s, 31);
        for (int n = 2; wb.Worksheets.Any(w => w.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); n++)
        {
            var suffix = " (" + n + ")";
            candidate = Trunc(s, 31 - suffix.Length) + suffix;
        }
        return candidate;
    }
}
