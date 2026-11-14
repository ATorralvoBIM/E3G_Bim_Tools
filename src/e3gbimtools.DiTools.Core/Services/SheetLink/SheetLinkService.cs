using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Logging;
using E3G.BimTools.DiTools.Core.Models.SheetLink;

namespace E3G.BimTools.DiTools.Core.Services.SheetLink;

/// <summary>
/// Implementacion de <see cref="ISheetLinkService"/> para Sprint 11.
///
/// Export: lee todos los ViewSheet del documento y genera un .xlsx con:
///   - Hoja "Planos": tabla formateada con cabecera azul corporativa y datos editables.
///     La primera columna (ElementId) tiene fondo gris para indicar que es de solo
///     lectura; las demas columnas son totalmente editables en Excel.
///   - Hoja "_Meta" (oculta): metadatos de exportacion (fecha, documento, hash de
///     columnas) usados en Sprint 12 para validar el archivo al re-importar.
///
/// Import: lee el .xlsx editado (hoja "Planos"), valida los ElementIds y actualiza
/// los parametros del plano mediante <see cref="ITransactionService"/>.
/// Un unico Ctrl-Z deshace toda la operacion.
/// </summary>
public sealed class SheetLinkService : ISheetLinkService
{
    // ====================================================================
    // Constantes
    // ====================================================================

    private const string SheetDataName = "Planos";
    private const string MetaSheetName = "_Meta";

    private static readonly string[] DataColumns =
    {
        "ElementId",
        "SheetNumber",
        "SheetName",
        "TitleBlock",
        "RevisionDate",
        "RevisionNumber",
        "RevisionDescription",
        "IssuedBy",
        "IssuedTo",
        "ApprovedBy",
        "DesignedBy",
        "CheckedBy",
        "DrawnBy",
        "ViewsPlaced",
    };

    private static readonly HashSet<string> ReadOnlyColumns =
        new(StringComparer.OrdinalIgnoreCase) { "ElementId", "TitleBlock", "ViewsPlaced" };

    // ====================================================================
    // Ctor
    // ====================================================================
    private readonly ITransactionService _tx;
    private readonly ILoggerFactory      _logFac;

    public SheetLinkService(ITransactionService tx, ILoggerFactory logFac)
    {
        _tx     = tx     ?? throw new ArgumentNullException(nameof(tx));
        _logFac = logFac ?? throw new ArgumentNullException(nameof(logFac));
    }

    // ====================================================================
    // ReadSheets
    // ====================================================================
    public IReadOnlyList<SheetLinkRow> ReadSheets(Document doc)
    {
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(vs => !vs.IsPlaceholder)
            .OrderBy(vs => vs.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<SheetLinkRow>(sheets.Count);
        foreach (var vs in sheets)
            rows.Add(BuildRow(doc, vs));

        return rows;
    }

    // ====================================================================
    // ExportToXlsx
    // ====================================================================
    public SheetLinkExportResult ExportToXlsx(Document doc, SheetLinkExportOptions options)
    {
        try
        {
            var rows = ReadSheets(doc);

            if (options.FilteredElementIds is { Count: > 0 })
            {
                var set = new HashSet<int>(options.FilteredElementIds);
                rows = rows.Where(r => set.Contains(r.ElementId)).ToList();
            }

            using var wb = new XLWorkbook();

            // 1. Hoja principal
            var ws = wb.Worksheets.Add(SheetDataName);
            WriteDataSheet(ws, rows, options);

            // 2. Hoja de metadatos (oculta)
            var wsMeta = wb.Worksheets.Add(MetaSheetName);
            WriteMetaSheet(wsMeta, doc, rows.Count);
            wsMeta.Visibility = XLWorksheetVisibility.Hidden;

            // 3. Guardar
            var outputPath = options.OutputPath
                ?? Path.Combine(Path.GetTempPath(),
                       $"SheetLink_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            wb.SaveAs(outputPath);

            return new SheetLinkExportResult
            {
                Success        = true,
                SheetsExported = rows.Count,
                OutputPath     = outputPath
            };
        }
        catch (Exception ex)
        {
            return new SheetLinkExportResult
            {
                Success      = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ====================================================================
    // ParseXlsx
    // ====================================================================
    public IReadOnlyList<SheetLinkRow> ParseXlsx(string xlsxPath)
    {
        if (string.IsNullOrWhiteSpace(xlsxPath))
            return Array.Empty<SheetLinkRow>();
        if (!File.Exists(xlsxPath))
            throw new FileNotFoundException($"No se encontro el archivo: {xlsxPath}", xlsxPath);

        using var wb = new XLWorkbook(xlsxPath);

        if (!wb.TryGetWorksheet(SheetDataName, out var ws))
            throw new InvalidOperationException(
                $"El archivo no contiene la hoja '{SheetDataName}'. " +
                "Asegurate de que el .xlsx fue exportado por SheetLink.");

        var usedRange = ws.RangeUsed();
        if (usedRange is null || usedRange.RowCount() < 2)
            return Array.Empty<SheetLinkRow>();

        int rowCount = usedRange.RowCount();
        int colCount = usedRange.ColumnCount();

        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 1; c <= colCount; c++)
        {
            var header = ws.Cell(1, c).GetString().Trim();
            if (!string.IsNullOrEmpty(header))
                colIndex[header] = c;
        }

        var result = new List<SheetLinkRow>(rowCount - 1);
        for (int r = 2; r <= rowCount; r++)
        {
            var elementIdStr = GetCell(ws, r, colIndex, "ElementId");
            if (string.IsNullOrWhiteSpace(elementIdStr)) continue;
            if (!int.TryParse(elementIdStr, out var elementId)) continue;

            result.Add(new SheetLinkRow
            {
                ElementId           = elementId,
                SheetNumber         = GetCell(ws, r, colIndex, "SheetNumber"),
                SheetName           = GetCell(ws, r, colIndex, "SheetName"),
                TitleBlock          = GetCell(ws, r, colIndex, "TitleBlock"),
                RevisionDate        = GetCell(ws, r, colIndex, "RevisionDate"),
                RevisionNumber      = GetCell(ws, r, colIndex, "RevisionNumber"),
                RevisionDescription = GetCell(ws, r, colIndex, "RevisionDescription"),
                IssuedBy            = GetCell(ws, r, colIndex, "IssuedBy"),
                IssuedTo            = GetCell(ws, r, colIndex, "IssuedTo"),
                ApprovedBy          = GetCell(ws, r, colIndex, "ApprovedBy"),
                DesignedBy          = GetCell(ws, r, colIndex, "DesignedBy"),
                CheckedBy           = GetCell(ws, r, colIndex, "CheckedBy"),
                DrawnBy             = GetCell(ws, r, colIndex, "DrawnBy"),
                ViewsPlaced         = GetCell(ws, r, colIndex, "ViewsPlaced"),
            });
        }

        return result;
    }

    // ====================================================================
    // ApplyChanges
    // ====================================================================
    public SheetLinkImportResult ApplyChanges(Document doc, IReadOnlyList<SheetLinkRow> rows)
    {
        if (rows.Count == 0)
            return new SheetLinkImportResult { Success = true, Summary = "Sin filas para aplicar." };

        var sheetMap = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(vs => !vs.IsPlaceholder)
            .ToDictionary(vs => (int)vs.Id.Value, vs => vs);

        var mutableRows = rows.Select(Clone).ToList();
        int updated = 0, skipped = 0, errors = 0;

        try
        {
            _tx.Run(doc, "SheetLink — Aplicar cambios", _ =>
            {
                foreach (var row in mutableRows)
                {
                    if (!sheetMap.TryGetValue(row.ElementId, out var vs))
                    {
                        row.ImportStatus  = SheetLinkImportStatus.Skipped;
                        row.ImportMessage = $"No se encontro plano con ElementId={row.ElementId}";
                        skipped++;
                        continue;
                    }

                    try
                    {
                        SetStringParam(vs, BuiltInParameter.SHEET_NUMBER,      row.SheetNumber);
                        SetStringParam(vs, BuiltInParameter.SHEET_NAME,        row.SheetName);
                        SetStringParam(vs, BuiltInParameter.SHEET_ISSUE_DATE,  row.RevisionDate);
                        SetStringParam(vs, BuiltInParameter.SHEET_CURRENT_REVISION,             row.RevisionNumber);
                        SetStringParam(vs, BuiltInParameter.SHEET_CURRENT_REVISION_DESCRIPTION, row.RevisionDescription);
                        SetNamedStringParam(vs, "Issued by",   row.IssuedBy);
                        SetNamedStringParam(vs, "Issued to",   row.IssuedTo);
                        SetStringParam(vs, BuiltInParameter.SHEET_APPROVED_BY, row.ApprovedBy);
                        SetStringParam(vs, BuiltInParameter.SHEET_DESIGNED_BY, row.DesignedBy);
                        SetStringParam(vs, BuiltInParameter.SHEET_CHECKED_BY,  row.CheckedBy);
                        SetStringParam(vs, BuiltInParameter.SHEET_DRAWN_BY,    row.DrawnBy);

                        row.ImportStatus = SheetLinkImportStatus.Updated;
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        row.ImportStatus  = SheetLinkImportStatus.Error;
                        row.ImportMessage = ex.Message;
                        errors++;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            return new SheetLinkImportResult
            {
                Success = false,
                Summary = $"Error en la transaccion: {ex.Message}",
                Updated = updated,
                Skipped = skipped,
                Errors  = errors,
                Rows    = mutableRows
            };
        }

        return new SheetLinkImportResult
        {
            Success = errors == 0,
            Updated = updated,
            Skipped = skipped,
            Errors  = errors,
            Summary = $"{updated} planos actualizados, {skipped} omitidos, {errors} errores.",
            Rows    = mutableRows
        };
    }

    // ====================================================================
    // Escritura de la hoja principal
    // ====================================================================

    private static void WriteDataSheet(
        IXLWorksheet ws,
        IReadOnlyList<SheetLinkRow> rows,
        SheetLinkExportOptions options)
    {
        var headerBg     = XLColor.FromArgb(0x1F, 0x4E, 0x79);
        var headerFg     = XLColor.White;
        var readOnlyBg   = XLColor.FromArgb(0xF2, 0xF2, 0xF2);
        var readOnlyFg   = XLColor.FromArgb(0x59, 0x59, 0x59);
        var altRowBg     = XLColor.FromArgb(0xDE, 0xEB, 0xF7);

        var cols = DataColumns
            .Where(c =>
            {
                if (!options.IncludeRevisionFields &&
                    (c == "RevisionDate" || c == "RevisionNumber" || c == "RevisionDescription"))
                    return false;
                if (!options.IncludeSignatoryFields &&
                    (c == "IssuedBy" || c == "IssuedTo" || c == "ApprovedBy" ||
                     c == "DesignedBy" || c == "CheckedBy" || c == "DrawnBy"))
                    return false;
                if (!options.IncludeViewsPlaced && c == "ViewsPlaced")
                    return false;
                return true;
            })
            .ToArray();

        // Cabecera
        for (int c = 0; c < cols.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cols[c];
            cell.Style.Font.Bold                = true;
            cell.Style.Font.FontColor           = headerFg;
            cell.Style.Fill.BackgroundColor     = headerBg;
            cell.Style.Alignment.Horizontal     = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.BottomBorder      = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorderColor = XLColor.White;
        }

        ws.SheetView.FreezeRows(1);

        // Datos
        for (int r = 0; r < rows.Count; r++)
        {
            var row    = rows[r];
            var values = BuildValueMap(row, options);
            bool isAlt = (r % 2 == 1);

            for (int c = 0; c < cols.Length; c++)
            {
                var cell    = ws.Cell(r + 2, c + 1);
                var colName = cols[c];
                cell.Value  = values.TryGetValue(colName, out var v) ? v : "";
                cell.Style.Alignment.WrapText = false;

                if (ReadOnlyColumns.Contains(colName))
                {
                    cell.Style.Fill.BackgroundColor = readOnlyBg;
                    cell.Style.Font.FontColor       = readOnlyFg;
                }
                else if (isAlt)
                {
                    cell.Style.Fill.BackgroundColor = altRowBg;
                }
            }
        }

        // Anchura de columnas
        for (int c = 0; c < cols.Length; c++)
        {
            var col = ws.Column(c + 1);
            if (cols[c] == "ElementId")
            {
                col.Width = 10;
            }
            else
            {
                col.AdjustToContents(2, Math.Max(rows.Count + 1, 2));
                if (col.Width < 12) col.Width = 12;
                if (col.Width > 45) col.Width = 45;
            }
        }

        // Autofilter
        if (rows.Count > 0)
            ws.RangeUsed()?.SetAutoFilter();
    }

    // ====================================================================
    // Escritura de la hoja de metadatos
    // ====================================================================

    private static void WriteMetaSheet(IXLWorksheet ws, Document doc, int sheetCount)
    {
        ws.Cell("A1").Value = "ExportDate";
        ws.Cell("B1").Value = DateTime.UtcNow.ToString("o");

        ws.Cell("A2").Value = "DocumentTitle";
        ws.Cell("B2").Value = doc.Title ?? "(sin titulo)";

        ws.Cell("A3").Value = "ColumnHash";
        ws.Cell("B3").Value = ComputeColumnHash(DataColumns);

        ws.Cell("A4").Value = "SheetsExported";
        ws.Cell("B4").Value = sheetCount;

        ws.Cell("A5").Value = "Generator";
        ws.Cell("B5").Value = "e3gbimtools.DiTools SheetLink Sprint 11";

        ws.Columns("A").Width = 18;
        ws.Columns("B").AdjustToContents();
    }

    // ====================================================================
    // Helpers — celdas
    // ====================================================================

    private static string GetCell(
        IXLWorksheet ws, int row,
        Dictionary<string, int> colIndex, string colName)
    {
        if (!colIndex.TryGetValue(colName, out var c)) return string.Empty;
        return ws.Cell(row, c).GetString().Trim();
    }

    // ====================================================================
    // Helpers — construccion de filas
    // ====================================================================

    private static SheetLinkRow BuildRow(Document doc, ViewSheet vs)
    {
        var viewNames = vs.GetAllPlacedViews()
            .Select(id => doc.GetElement(id))
            .Where(e => e != null)
            .Select(e => e.Name ?? "")
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var titleBlock = new FilteredElementCollector(doc, vs.Id)
            .OfClass(typeof(FamilyInstance))
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .Cast<FamilyInstance>()
            .FirstOrDefault();

        return new SheetLinkRow
        {
            ElementId           = (int)vs.Id.Value,
            SheetNumber         = GetStringParam(vs, BuiltInParameter.SHEET_NUMBER)      ?? vs.SheetNumber,
            SheetName           = GetStringParam(vs, BuiltInParameter.SHEET_NAME)        ?? vs.Name,
            TitleBlock          = titleBlock?.Symbol?.FamilyName ?? "",
            RevisionDate        = GetStringParam(vs, BuiltInParameter.SHEET_ISSUE_DATE)  ?? "",
            RevisionNumber      = GetStringParam(vs, BuiltInParameter.SHEET_CURRENT_REVISION) ?? "",
            RevisionDescription = GetStringParam(vs, BuiltInParameter.SHEET_CURRENT_REVISION_DESCRIPTION) ?? "",
            IssuedBy            = GetNamedStringParam(vs, "Issued by")  ?? "",
            IssuedTo            = GetNamedStringParam(vs, "Issued to")  ?? "",
            ApprovedBy          = GetStringParam(vs, BuiltInParameter.SHEET_APPROVED_BY) ?? "",
            DesignedBy          = GetStringParam(vs, BuiltInParameter.SHEET_DESIGNED_BY) ?? "",
            CheckedBy           = GetStringParam(vs, BuiltInParameter.SHEET_CHECKED_BY)  ?? "",
            DrawnBy             = GetStringParam(vs, BuiltInParameter.SHEET_DRAWN_BY)    ?? "",
            ViewsPlaced         = string.Join(" | ", viewNames)
        };
    }

    private static Dictionary<string, string> BuildValueMap(
        SheetLinkRow row, SheetLinkExportOptions options)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ElementId"]           = row.ElementId.ToString(),
            ["SheetNumber"]         = row.SheetNumber,
            ["SheetName"]           = row.SheetName,
            ["TitleBlock"]          = row.TitleBlock,
            ["RevisionDate"]        = options.IncludeRevisionFields  ? row.RevisionDate        : "",
            ["RevisionNumber"]      = options.IncludeRevisionFields  ? row.RevisionNumber      : "",
            ["RevisionDescription"] = options.IncludeRevisionFields  ? row.RevisionDescription : "",
            ["IssuedBy"]            = options.IncludeSignatoryFields ? row.IssuedBy            : "",
            ["IssuedTo"]            = options.IncludeSignatoryFields ? row.IssuedTo            : "",
            ["ApprovedBy"]          = options.IncludeSignatoryFields ? row.ApprovedBy          : "",
            ["DesignedBy"]          = options.IncludeSignatoryFields ? row.DesignedBy          : "",
            ["CheckedBy"]           = options.IncludeSignatoryFields ? row.CheckedBy           : "",
            ["DrawnBy"]             = options.IncludeSignatoryFields ? row.DrawnBy             : "",
            ["ViewsPlaced"]         = options.IncludeViewsPlaced     ? row.ViewsPlaced         : "",
        };
    }

    // ====================================================================
    // Helpers — parametros Revit
    // ====================================================================

    private static string? GetStringParam(Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        return p?.StorageType == StorageType.String ? p.AsString() : null;
    }

    private static void SetStringParam(Element e, BuiltInParameter bip, string value)
    {
        var p = e.get_Parameter(bip);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return;
        p.Set(value ?? "");
    }

    private static string? GetNamedStringParam(Element e, string paramName)
    {
        var p = e.LookupParameter(paramName);
        return p?.StorageType == StorageType.String ? p.AsString() : null;
    }

    private static void SetNamedStringParam(Element e, string paramName, string value)
    {
        var p = e.LookupParameter(paramName);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return;
        p.Set(value ?? "");
    }

    // ====================================================================
    // Helpers — misc
    // ====================================================================

    private static SheetLinkRow Clone(SheetLinkRow r) => new()
    {
        ElementId           = r.ElementId,
        SheetNumber         = r.SheetNumber,
        SheetName           = r.SheetName,
        TitleBlock          = r.TitleBlock,
        RevisionDate        = r.RevisionDate,
        RevisionNumber      = r.RevisionNumber,
        RevisionDescription = r.RevisionDescription,
        IssuedBy            = r.IssuedBy,
        IssuedTo            = r.IssuedTo,
        ApprovedBy          = r.ApprovedBy,
        DesignedBy          = r.DesignedBy,
        CheckedBy           = r.CheckedBy,
        DrawnBy             = r.DrawnBy,
        ViewsPlaced         = r.ViewsPlaced,
        ImportStatus        = r.ImportStatus,
        ImportMessage       = r.ImportMessage,
    };

    private static string ComputeColumnHash(IEnumerable<string> columns)
    {
        var raw  = string.Join(",", columns);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..8];
    }

    // ====================================================================
    // Sprint 12 — ValidateXlsxSchema
    // ====================================================================

    public XlsxValidationResult ValidateXlsxSchema(string xlsxPath)
    {
        if (string.IsNullOrWhiteSpace(xlsxPath))
            return new XlsxValidationResult { IsValid = false, ErrorMessage = "Ruta de archivo vacia." };

        if (!File.Exists(xlsxPath))
            return new XlsxValidationResult { IsValid = false, ErrorMessage = $"Archivo no encontrado: {xlsxPath}" };

        try
        {
            using var wb = new XLWorkbook(xlsxPath);

            if (!wb.TryGetWorksheet(SheetDataName, out _))
                return new XlsxValidationResult
                {
                    IsValid      = false,
                    ErrorMessage = $"El archivo no contiene la hoja '{SheetDataName}'. " +
                                   "Asegurate de que el .xlsx fue exportado por SheetLink."
                };

            if (!wb.TryGetWorksheet(MetaSheetName, out var wsMeta))
                return new XlsxValidationResult
                {
                    IsValid      = false,
                    ErrorMessage = $"No se encontro la hoja de metadatos '{MetaSheetName}'. " +
                                   "El archivo puede no haber sido exportado por SheetLink, " +
                                   "o la hoja _Meta fue eliminada manualmente."
                };

            var meta         = ReadMetaSheet(wsMeta);
            var expectedHash = ComputeColumnHash(DataColumns);

            if (!string.IsNullOrEmpty(meta.ColumnHash) && meta.ColumnHash != expectedHash)
                return new XlsxValidationResult
                {
                    IsValid        = false,
                    ErrorMessage   = $"Esquema incompatible: el archivo tiene hash '{meta.ColumnHash}' " +
                                     $"pero la version actual de SheetLink espera '{expectedHash}'. " +
                                     "El archivo puede haber sido exportado con una version diferente.",
                    ExportDate     = meta.ExportDate,
                    DocumentTitle  = meta.DocumentTitle,
                    ColumnHash     = meta.ColumnHash,
                    SheetsExported = meta.SheetsExported,
                };

            return new XlsxValidationResult
            {
                IsValid        = true,
                ExportDate     = meta.ExportDate,
                DocumentTitle  = meta.DocumentTitle,
                SheetsExported = meta.SheetsExported,
                ColumnHash     = meta.ColumnHash,
            };
        }
        catch (Exception ex)
        {
            return new XlsxValidationResult
            {
                IsValid      = false,
                ErrorMessage = $"Error al leer el archivo: {ex.Message}"
            };
        }
    }

    // ====================================================================
    // Sprint 12 — ComputeDiff
    // ====================================================================

    public IReadOnlyList<SheetLinkDiffRow> ComputeDiff(Document doc, string xlsxPath)
    {
        var xlsxRows = ParseXlsx(xlsxPath);
        var modelMap = ReadSheets(doc).ToDictionary(r => r.ElementId, r => r);

        var result = new List<SheetLinkDiffRow>(xlsxRows.Count);

        foreach (var xr in xlsxRows)
        {
            if (!modelMap.TryGetValue(xr.ElementId, out var mr))
            {
                result.Add(new SheetLinkDiffRow
                {
                    ElementId         = xr.ElementId,
                    Status            = DiffStatus.Missing,
                    IsSelected        = false,
                    SheetNumber_After = xr.SheetNumber,
                    SheetName_After   = xr.SheetName,
                });
                continue;
            }

            var diff = new SheetLinkDiffRow
            {
                ElementId                  = xr.ElementId,
                SheetNumber_Before         = mr.SheetNumber,
                SheetNumber_After          = xr.SheetNumber,
                SheetName_Before           = mr.SheetName,
                SheetName_After            = xr.SheetName,
                RevisionDate_Before        = mr.RevisionDate,
                RevisionDate_After         = xr.RevisionDate,
                RevisionNumber_Before      = mr.RevisionNumber,
                RevisionNumber_After       = xr.RevisionNumber,
                RevisionDescription_Before = mr.RevisionDescription,
                RevisionDescription_After  = xr.RevisionDescription,
                IssuedBy_Before            = mr.IssuedBy,
                IssuedBy_After             = xr.IssuedBy,
                IssuedTo_Before            = mr.IssuedTo,
                IssuedTo_After             = xr.IssuedTo,
                ApprovedBy_Before          = mr.ApprovedBy,
                ApprovedBy_After           = xr.ApprovedBy,
                DesignedBy_Before          = mr.DesignedBy,
                DesignedBy_After           = xr.DesignedBy,
                CheckedBy_Before           = mr.CheckedBy,
                CheckedBy_After            = xr.CheckedBy,
                DrawnBy_Before             = mr.DrawnBy,
                DrawnBy_After              = xr.DrawnBy,
            };

            diff.Status    = diff.HasChanges ? DiffStatus.Modified : DiffStatus.Unchanged;
            diff.IsSelected = diff.Status == DiffStatus.Modified;
            result.Add(diff);
        }

        return result;
    }

    // ====================================================================
    // Sprint 12 — ApplyDiff
    // ====================================================================

    public SheetLinkImportResult ApplyDiff(Document doc, IReadOnlyList<SheetLinkDiffRow> rows)
    {
        var toApply = rows
            .Where(r => r.IsSelected && r.Status == DiffStatus.Modified)
            .ToList();

        if (toApply.Count == 0)
        {
            foreach (var r in rows)
                if (r.ApplyStatus == SheetLinkImportStatus.Pending)
                    r.ApplyStatus = SheetLinkImportStatus.Skipped;

            return new SheetLinkImportResult
            {
                Success = true,
                Summary = "No hay filas seleccionadas para aplicar.",
                Skipped = rows.Count,
            };
        }

        var sheetMap = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(vs => !vs.IsPlaceholder)
            .ToDictionary(vs => (int)vs.Id.Value, vs => vs);

        int updated = 0, skipped = 0, errors = 0;

        try
        {
            _tx.Run(doc, "SheetLink — Aplicar diff", _ =>
            {
                foreach (var diff in toApply)
                {
                    if (!sheetMap.TryGetValue(diff.ElementId, out var vs))
                    {
                        diff.ApplyStatus  = SheetLinkImportStatus.Skipped;
                        diff.ApplyMessage = $"ElementId={diff.ElementId} ya no existe en el modelo.";
                        skipped++;
                        continue;
                    }

                    try
                    {
                        SetStringParam(vs, BuiltInParameter.SHEET_NUMBER,                         diff.SheetNumber_After);
                        SetStringParam(vs, BuiltInParameter.SHEET_NAME,                           diff.SheetName_After);
                        SetStringParam(vs, BuiltInParameter.SHEET_ISSUE_DATE,                     diff.RevisionDate_After);
                        SetStringParam(vs, BuiltInParameter.SHEET_CURRENT_REVISION,               diff.RevisionNumber_After);
                        SetStringParam(vs, BuiltInParameter.SHEET_CURRENT_REVISION_DESCRIPTION,   diff.RevisionDescription_After);
                        SetNamedStringParam(vs, "Issued by",                                      diff.IssuedBy_After);
                        SetNamedStringParam(vs, "Issued to",                                      diff.IssuedTo_After);
                        SetStringParam(vs, BuiltInParameter.SHEET_APPROVED_BY,                    diff.ApprovedBy_After);
                        SetStringParam(vs, BuiltInParameter.SHEET_DESIGNED_BY,                    diff.DesignedBy_After);
                        SetStringParam(vs, BuiltInParameter.SHEET_CHECKED_BY,                     diff.CheckedBy_After);
                        SetStringParam(vs, BuiltInParameter.SHEET_DRAWN_BY,                       diff.DrawnBy_After);

                        diff.ApplyStatus = SheetLinkImportStatus.Updated;
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        diff.ApplyStatus  = SheetLinkImportStatus.Error;
                        diff.ApplyMessage = ex.Message;
                        errors++;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            return new SheetLinkImportResult
            {
                Success = false,
                Summary = $"Error en la transaccion: {ex.Message}",
                Updated = updated,
                Skipped = skipped,
                Errors  = errors,
            };
        }

        // Marcar filas no seleccionadas como omitidas
        int notSelected = 0;
        foreach (var r in rows.Except(toApply))
        {
            if (r.ApplyStatus == SheetLinkImportStatus.Pending)
            {
                r.ApplyStatus = SheetLinkImportStatus.Skipped;
                notSelected++;
            }
        }

        int totalSkipped = skipped + notSelected;
        return new SheetLinkImportResult
        {
            Success = errors == 0,
            Updated = updated,
            Skipped = totalSkipped,
            Errors  = errors,
            Summary = $"{updated} planos actualizados, {totalSkipped} omitidos, {errors} errores.",
        };
    }

    // ====================================================================
    // Helper — lectura de la hoja _Meta
    // ====================================================================

    private static (string? ExportDate, string? DocumentTitle, string? ColumnHash, int SheetsExported)
        ReadMetaSheet(IXLWorksheet wsMeta)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var used = wsMeta.RangeUsed();
        if (used is not null)
        {
            for (int r = 1; r <= used.RowCount(); r++)
            {
                var key = wsMeta.Cell(r, 1).GetString().Trim();
                var val = wsMeta.Cell(r, 2).GetString().Trim();
                if (!string.IsNullOrEmpty(key))
                    dict[key] = val;
            }
        }

        int.TryParse(dict.GetValueOrDefault("SheetsExported", "0"), out int count);

        return (
            dict.GetValueOrDefault("ExportDate"),
            dict.GetValueOrDefault("DocumentTitle"),
            dict.GetValueOrDefault("ColumnHash"),
            count
        );
    }
}
