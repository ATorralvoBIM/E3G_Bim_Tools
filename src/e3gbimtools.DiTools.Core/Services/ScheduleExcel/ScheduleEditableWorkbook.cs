using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Formato "editable" de una tabla: una fila por elemento, con su ElementId,
/// para rellenar datos en Excel y volver a importarlos.
///
///   Fila 1  titulo (formato E3G)
///   Fila 2  instrucciones
///   Fila 3  encabezados: ElementId + campos. Azul = editable, gris = solo lectura.
///   Fila 4+ datos
///   Hojas ocultas "_Meta" (tabla, documento, fecha) y "_Columns" (definicion
///   de cada columna: parametro, tipo/ejemplar, unidades) para importar.
///
/// Las columnas se casan por el texto del encabezado, asi que el usuario puede
/// reordenarlas u ocultarlas en Excel. No depende de la API de Revit.
/// </summary>
public static class ScheduleEditableWorkbook
{
    public const string ColumnsSheetName = "_Columns";
    public const string IdHeading = "ElementId";
    public const string ModeEditable = "Editable";
    private const int HeadingRow = 3;

    private static readonly XLColor TitleBg    = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor EditableBg = XLColor.FromHtml("#D9E1F2");
    private static readonly XLColor ReadOnlyHeadBg = XLColor.FromHtml("#D9D9D9");
    private static readonly XLColor ReadOnlyBg = XLColor.FromHtml("#F2F2F2");
    private static readonly XLColor MutedFg    = XLColor.FromHtml("#595959");

    // ====================================================================
    // Escritura
    // ====================================================================

    public static void Write(ScheduleEditableData data, string filePath)
    {
        using var wb = new XLWorkbook();
        WriteSheet(wb, data);
        wb.SaveAs(filePath);
    }

    /// <summary>Añade la tabla como una pestaña editable nueva del libro.</summary>
    public static void WriteSheet(XLWorkbook wb, ScheduleEditableData data)
    {
        if (wb is null) throw new ArgumentNullException(nameof(wb));
        if (data is null) throw new ArgumentNullException(nameof(data));
        MakeHeadingsUnique(data.Columns);

        var ws = wb.Worksheets.Add(ScheduleWorkbookMeta.UniqueSheetName(wb, data.Name));
        int nc = data.Columns.Count + 1;

        // Titulo + instrucciones
        var title = ws.Cell(1, 1);
        title.Value = data.Name;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 13;
        title.Style.Font.FontColor = XLColor.White;
        title.Style.Fill.BackgroundColor = TitleBg;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        var help = ws.Cell(2, 1);
        help.Value = "Rellena o cambia las columnas azules y vuelve a importar el archivo desde «TableLink». " +
                     "Las grises son de solo lectura. No cambies la columna ElementId ni los encabezados.";
        help.Style.Font.Italic = true;
        help.Style.Font.FontColor = MutedFg;
        if (nc > 1)
        {
            ws.Range(1, 1, 1, nc).Merge();
            ws.Range(2, 1, 2, nc).Merge();
        }

        // Encabezados
        Heading(ws.Cell(HeadingRow, 1), IdHeading, editable: false);
        for (int i = 0; i < data.Columns.Count; i++)
        {
            var col = data.Columns[i];
            var cell = ws.Cell(HeadingRow, i + 2);
            Heading(cell, col.Heading, col.Editable);
            if (col.IsType)
                cell.CreateComment().AddText("Parámetro de tipo: el valor se aplica a todos los elementos de ese tipo.");
            else if (!col.Editable)
                cell.CreateComment().AddText("Solo lectura (parámetro calculado, bloqueado o referencia a otro elemento).");
        }

        // Datos
        int r = HeadingRow + 1;
        foreach (var row in data.Rows)
        {
            var idCell = ws.Cell(r, 1);
            idCell.Value = row.ElementId;
            idCell.Style.Fill.BackgroundColor = ReadOnlyBg;
            idCell.Style.Font.FontColor = MutedFg;
            idCell.Style.NumberFormat.Format = "0";

            for (int i = 0; i < data.Columns.Count; i++)
            {
                var col = data.Columns[i];
                var cell = ws.Cell(r, i + 2);
                object? v = i < row.Values.Count ? row.Values[i] : null;
                switch (v)
                {
                    case null: break;
                    case double d: cell.Value = d; break;
                    case bool b: cell.Value = b ? "Sí" : "No"; break;
                    default: cell.Value = Convert.ToString(v, CultureInfo.InvariantCulture); break;
                }
                if (col.Kind == EditableKind.Number)
                    cell.Style.NumberFormat.Format = col.Decimals > 0 ? "0." + new string('0', col.Decimals) : "0";
                if (col.Kind == EditableKind.Integer)
                    cell.Style.NumberFormat.Format = "0";
                if (!col.Editable) cell.Style.Fill.BackgroundColor = ReadOnlyBg;
            }
            r++;
        }

        int lastRow = Math.Max(HeadingRow + 1, r - 1);
        var table = ws.Range(HeadingRow, 1, lastRow, nc);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        if (data.Rows.Count > 0) ws.Range(HeadingRow, 1, r - 1, nc).SetAutoFilter();

        // Desplegable Sí/No
        for (int i = 0; i < data.Columns.Count; i++)
        {
            if (data.Columns[i].Kind != EditableKind.YesNo || !data.Columns[i].Editable || data.Rows.Count == 0) continue;
            ws.Range(HeadingRow + 1, i + 2, r - 1, i + 2).CreateDataValidation().List("\"Sí,No\"", true);
        }

        // Anchos
        ws.Column(1).Width = 11;
        for (int i = 0; i < data.Columns.Count; i++)
        {
            int len = data.Columns[i].Heading.Length;
            foreach (var row in data.Rows.Take(500))
                if (i < row.Values.Count && row.Values[i] is string s && s.Length > len) len = s.Length;
            ws.Column(i + 2).Width = Math.Min(60, Math.Max(10, len * 1.1 + 2));
        }
        ws.SheetView.FreezeRows(HeadingRow);
        ws.SheetView.FreezeColumns(1);

        ScheduleWorkbookMeta.Add(wb, ws.Name, new[]
        {
            ("Mode", ModeEditable),
            ("ScheduleName", data.Name),
            ("ScheduleUniqueId", data.ScheduleUniqueId ?? ""),
            ("Document", data.DocumentTitle ?? ""),
            ("HeadingRow", HeadingRow.ToString(CultureInfo.InvariantCulture)),
        });
        AppendColumns(wb, ws.Name, data.Columns);
    }

    private static void Heading(IXLCell cell, string text, bool editable)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = editable ? EditableBg : ReadOnlyHeadBg;
        if (!editable) cell.Style.Font.FontColor = MutedFg;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.WrapText = true;
    }

    private static void MakeHeadingsUnique(List<EditableColumn> cols)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { IdHeading };
        foreach (var c in cols)
        {
            var baseName = string.IsNullOrWhiteSpace(c.Heading) ? "Campo" : c.Heading.Trim();
            var name = baseName;
            for (int n = 2; !seen.Add(name); n++) name = baseName + " (" + n + ")";
            c.Heading = name;
        }
    }

    private static readonly string[] ColumnFields =
        { "Sheet", "Heading", "Key", "ParameterId", "IsType", "Kind", "Editable", "UnitTypeId", "SpecTypeId", "Decimals" };

    internal static void AppendColumns(XLWorkbook wb, string sheet, IReadOnlyList<EditableColumn> cols)
    {
        if (!wb.TryGetWorksheet(ColumnsSheetName, out var ws))
        {
            ws = wb.Worksheets.Add(ColumnsSheetName);
            for (int c = 0; c < ColumnFields.Length; c++) ws.Cell(1, c + 1).Value = ColumnFields[c];
            ws.Visibility = XLWorksheetVisibility.Hidden;
        }
        int r = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
        foreach (var col in cols)
        {
            ws.Cell(r, 1).Value = sheet;
            ws.Cell(r, 2).Value = col.Heading;
            ws.Cell(r, 3).Value = col.Key;
            ws.Cell(r, 4).Value = col.ParameterId.ToString(CultureInfo.InvariantCulture);
            ws.Cell(r, 5).Value = col.IsType ? "1" : "0";
            ws.Cell(r, 6).Value = col.Kind.ToString();
            ws.Cell(r, 7).Value = col.Editable ? "1" : "0";
            ws.Cell(r, 8).Value = col.UnitTypeId ?? "";
            ws.Cell(r, 9).Value = col.SpecTypeId ?? "";
            ws.Cell(r, 10).Value = col.Decimals.ToString(CultureInfo.InvariantCulture);
            r++;
        }
    }

    // ====================================================================
    // Lectura
    // ====================================================================

    /// <summary>
    /// Lee todas las pestañas editables del libro. Lanza
    /// <see cref="InvalidOperationException"/> si el archivo no es de «TableLink».
    /// </summary>
    public static ParsedEditableFile Read(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        var file = new ParsedEditableFile();

        var meta = ScheduleWorkbookMeta.Read(wb);
        if (meta.Count == 0)
            throw new InvalidOperationException("El archivo no se generó con «TableLink» (falta la hoja _Meta).");

        var columns = new Dictionary<string, List<EditableColumn>>(StringComparer.OrdinalIgnoreCase);
        if (wb.TryGetWorksheet(ColumnsSheetName, out var colsWs))
        {
            foreach (var row in colsWs.RowsUsed().Skip(1))
            {
                var sheet = row.Cell(1).GetString();
                if (!columns.TryGetValue(sheet, out var list)) columns[sheet] = list = new List<EditableColumn>();
                list.Add(new EditableColumn
                {
                    Heading = row.Cell(2).GetString(),
                    Key = row.Cell(3).GetString(),
                    ParameterId = long.TryParse(row.Cell(4).GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ? pid : 0,
                    IsType = row.Cell(5).GetString() == "1",
                    Kind = Enum.TryParse<EditableKind>(row.Cell(6).GetString(), out var kind) ? kind : EditableKind.Text,
                    Editable = row.Cell(7).GetString() == "1",
                    UnitTypeId = NullIfEmpty(row.Cell(8).GetString()),
                    SpecTypeId = NullIfEmpty(row.Cell(9).GetString()),
                    Decimals = int.TryParse(row.Cell(10).GetString(), out var dec) ? dec : 0,
                });
            }
        }

        var rowMaps = ScheduleRowsSheet.Read(wb);
        foreach (var (sheetName, values) in meta)
        {
            if (!wb.TryGetWorksheet(sheetName, out var ws)) continue;
            var cols = columns.TryGetValue(sheetName, out var cl) ? cl : new List<EditableColumn>();
            if (values.TryGetValue("Mode", out var mode) && mode == ModeEditable)
            {
                file.Sheets.Add(ReadSheet(ws, values, cols));
            }
            else if (rowMaps.TryGetValue(sheetName, out var rows) && cols.Count > 0)
            {
                file.PresentationSheetsData.Add(ReadPresentationSheet(ws, values, cols, rows));
            }
            else
            {
                file.PresentationSheets.Add(sheetName);
            }
        }

        if (file.Sheets.Count == 0 && file.PresentationSheetsData.Count == 0)
            throw new InvalidOperationException(file.PresentationSheets.Count > 0
                ? "Este Excel se exportó «como en Revit» con una versión anterior de la herramienta, que no guardaba " +
                  "qué elementos forman cada fila. Vuelve a exportar la tabla y repite los cambios en el Excel nuevo."
                : "El libro no contiene ninguna tabla importable.");
        return file;
    }

    private static ParsedPresentationSheet ReadPresentationSheet(IXLWorksheet ws, Dictionary<string, string> meta,
        List<EditableColumn> columns, List<PresentationRow> rows)
    {
        var sheet = new ParsedPresentationSheet { SheetName = ws.Name };
        foreach (var kv in meta) sheet.Meta[kv.Key] = kv.Value;
        sheet.Columns.AddRange(columns);
        if (meta.TryGetValue("DecimalSeparator", out var ds) && ds.Length == 1) sheet.DecimalSeparator = ds[0];
        sheet.GroupSeparator = meta.TryGetValue("GroupSeparator", out var gs) && gs.Length == 1 ? gs[0] : null;

        foreach (var row in rows)
        {
            for (int c = 0; c < columns.Count; c++)
                row.Values.Add(CellValue(ws.Cell(row.ExcelRow, c + 1)));
            sheet.Rows.Add(row);
        }
        return sheet;
    }

    private static ParsedEditableWorkbook ReadSheet(IXLWorksheet data, Dictionary<string, string> meta, List<EditableColumn> columns)
    {
        var result = new ParsedEditableWorkbook { SheetName = data.Name };
        foreach (var kv in meta) result.Meta[kv.Key] = kv.Value;
        result.Columns.AddRange(columns);

        int headingRow = result.Meta.TryGetValue("HeadingRow", out var hr) && int.TryParse(hr, out var h) ? h : HeadingRow;
        int lastCol = data.LastColumnUsed()?.ColumnNumber() ?? 0;

        // Encabezado → columna definida
        int idCol = 0;
        var map = new Dictionary<int, int>(); // columna Excel → indice en Columns
        for (int c = 1; c <= lastCol; c++)
        {
            var text = data.Cell(headingRow, c).GetString().Trim();
            if (text.Length == 0) continue;
            if (text.Equals(IdHeading, StringComparison.OrdinalIgnoreCase)) { idCol = c; continue; }
            int idx = result.Columns.FindIndex(x => x.Heading.Equals(text, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) map[c] = idx;
            else result.Warnings.Add(data.Name + ": columna «" + text + "» no reconocida, se ignora.");
        }
        if (idCol == 0)
        {
            result.Warnings.Add(data.Name + ": no se encuentra la columna ElementId, se ignora la pestaña.");
            return result;
        }
        result.IdExcelColumn = idCol;
        for (int i = 0; i < result.Columns.Count; i++)
        {
            result.ColumnExcelIndex.Add(map.FirstOrDefault(kv => kv.Value == i).Key);
            if (result.Columns[i].Editable && !map.ContainsValue(i))
                result.Warnings.Add(data.Name + ": falta la columna «" + result.Columns[i].Heading + "», no se importará.");
        }

        int lastRow = data.LastRowUsed()?.RowNumber() ?? headingRow;
        for (int r = headingRow + 1; r <= lastRow; r++)
        {
            var idCell = data.Cell(r, idCol);
            if (idCell.IsEmpty()) continue;
            long id;
            if (idCell.DataType == XLDataType.Number) id = (long)Math.Round(idCell.GetDouble());
            else if (!long.TryParse(idCell.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                result.Warnings.Add(data.Name + ", fila " + r + ": ElementId «" + idCell.GetString() + "» no válido, se ignora.");
                continue;
            }

            var row = new EditableRow { ElementId = id, ExcelRow = r };
            // Missing.Value = columna ausente del libro (no tocar); null = celda vacia.
            for (int i = 0; i < result.Columns.Count; i++) row.Values.Add(Missing.Value);
            foreach (var kv in map)
                row.Values[kv.Value] = CellValue(data.Cell(r, kv.Key));
            result.Rows.Add(row);
        }
        return result;
    }

    /// <summary>Valor de una celda: null (vacia), double, bool o string.</summary>
    private static object? CellValue(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        return cell.DataType switch
        {
            XLDataType.Number => cell.GetDouble(),
            XLDataType.Boolean => cell.GetBoolean(),
            XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => cell.GetString(),
        };
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
