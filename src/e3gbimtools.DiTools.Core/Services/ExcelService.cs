using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Core.Services;

/// <summary>
/// Implementacion de <see cref="IExcelService"/> basada en ClosedXML
/// (Sprint 6 — SheetGen).
///
/// Estrategia de columnas:
///   WriteSheet — escribe una cabecera con los nombres de las propiedades
///   publicas de T y a continuacion una fila por objeto.
///
///   ReadSheet  — lee la primera fila como cabecera y mapea cada columna
///   a la propiedad de T cuyo nombre coincida (case-insensitive).
///   Propiedades sin columna correspondiente se dejan en su valor por
///   defecto; columnas extra se ignoran.
/// </summary>
public sealed class ExcelService : IExcelService
{
    // ------------------------------------------------------------------ //
    //  WriteSheet                                                          //
    // ------------------------------------------------------------------ //

    public void WriteSheet<T>(string filePath, string sheetName, IEnumerable<T> rows)
    {
        if (filePath  is null) throw new ArgumentNullException(nameof(filePath));
        if (sheetName is null) throw new ArgumentNullException(nameof(sheetName));
        if (rows      is null) throw new ArgumentNullException(nameof(rows));

        var props = GetPublicProperties<T>();
        var rowList = rows.ToList();

        // Abre o crea el workbook
        IXLWorkbook wb;
        if (File.Exists(filePath))
        {
            wb = new XLWorkbook(filePath);
            // Elimina la hoja si ya existe para sobreescribir limpio
            if (wb.TryGetWorksheet(sheetName, out var existing))
                existing.Delete();
        }
        else
        {
            wb = new XLWorkbook();
        }

        var ws = wb.Worksheets.Add(sheetName);

        // Cabecera
        for (int c = 0; c < props.Length; c++)
            ws.Cell(1, c + 1).SetValue(props[c].Name)
              .Style.Font.SetBold(true);

        // Datos
        for (int r = 0; r < rowList.Count; r++)
        {
            var obj = rowList[r];
            for (int c = 0; c < props.Length; c++)
            {
                var value = props[c].GetValue(obj);
                SetCellValue(ws.Cell(r + 2, c + 1), value);
            }
        }

        // Autofit
        ws.Columns().AdjustToContents();

        // Asegura que el directorio existe
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        wb.SaveAs(filePath);
        wb.Dispose();
    }

    // ------------------------------------------------------------------ //
    //  ReadSheet                                                           //
    // ------------------------------------------------------------------ //

    public IEnumerable<T> ReadSheet<T>(string filePath, string sheetName) where T : new()
    {
        if (filePath  is null) throw new ArgumentNullException(nameof(filePath));
        if (sheetName is null) throw new ArgumentNullException(nameof(sheetName));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Archivo Excel no encontrado: '{filePath}'.", filePath);

        var props = GetPublicProperties<T>()
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        using var wb = new XLWorkbook(filePath);

        if (!wb.TryGetWorksheet(sheetName, out var ws))
            throw new InvalidOperationException(
                $"La hoja '{sheetName}' no existe en '{filePath}'. " +
                $"Hojas disponibles: {string.Join(", ", wb.Worksheets.Select(w => w.Name))}.");

        var usedRange = ws.RangeUsed();
        if (usedRange is null) yield break;

        int rowCount = usedRange.RowCount();
        int colCount = usedRange.ColumnCount();

        if (rowCount < 1) yield break;

        // Leer cabecera (fila 1)
        var headers = new Dictionary<int, string>(); // col index (1-based) → nombre
        for (int c = 1; c <= colCount; c++)
        {
            var header = ws.Cell(1, c).GetString().Trim();
            if (!string.IsNullOrEmpty(header))
                headers[c] = header;
        }

        // Leer datos (filas 2…n)
        for (int r = 2; r <= rowCount; r++)
        {
            // Saltar filas completamente vacías
            bool hasData = headers.Keys.Any(c => !ws.Cell(r, c).IsEmpty());
            if (!hasData) continue;

            var obj = new T();
            foreach (var (col, header) in headers)
            {
                if (!props.TryGetValue(header, out var prop)) continue;
                var rawValue = ws.Cell(r, col).Value;
                try { prop.SetValue(obj, ConvertValue(rawValue, prop.PropertyType)); }
                catch { /* ignora conversiones fallidas */ }
            }
            yield return obj;
        }
    }

    // ------------------------------------------------------------------ //
    //  Privado                                                             //
    // ------------------------------------------------------------------ //

    private static PropertyInfo[] GetPublicProperties<T>()
        => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.CanWrite)
                    .ToArray();

    private static void SetCellValue(IXLCell cell, object? value)
    {
        if (value is null)                   { cell.SetValue(string.Empty); return; }
        if (value is string s)               { cell.SetValue(s);            return; }
        if (value is int i)                  { cell.SetValue(i);            return; }
        if (value is double d)               { cell.SetValue(d);            return; }
        if (value is bool b)                 { cell.SetValue(b);            return; }
        if (value is DateTime dt)            { cell.SetValue(dt);           return; }
        cell.SetValue(value.ToString() ?? string.Empty);
    }

    private static object? ConvertValue(XLCellValue raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (raw.IsBlank)
            return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
                ? Activator.CreateInstance(targetType)
                : null;

        // string
        if (underlying == typeof(string))
            return raw.ToString();

        // numéricos
        if (underlying == typeof(int))
        {
            if (raw.IsNumber) return (int)raw.GetNumber();
            return int.TryParse(raw.ToString(), out var iv) ? iv : 0;
        }
        if (underlying == typeof(double))
        {
            if (raw.IsNumber) return raw.GetNumber();
            return double.TryParse(raw.ToString(), out var dv) ? dv : 0d;
        }
        if (underlying == typeof(bool))
        {
            if (raw.IsBoolean) return raw.GetBoolean();
            return bool.TryParse(raw.ToString(), out var bv) && bv;
        }

        // fallback: ToString y Convert
        return Convert.ChangeType(raw.ToString(), underlying);
    }
}
