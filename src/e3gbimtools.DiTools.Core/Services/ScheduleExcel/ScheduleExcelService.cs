using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;
using E3G.BimTools.DiTools.Core.Models.TableGen;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>Implementacion de <see cref="IScheduleExcelService"/>.</summary>
public sealed class ScheduleExcelService : IScheduleExcelService
{
    private readonly ILogger _logger;

    public ScheduleExcelService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<ScheduleExcelService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public IReadOnlyList<ScheduleInfo> GetSchedules(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
            .Select(s => new ScheduleInfo
            {
                Id = s.Id,
                Name = s.Name,
                Category = CategoryName(doc, s),
            })
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ScheduleSnapshot Read(ViewSchedule schedule) =>
        InReadTransaction(schedule.Document, () => ScheduleSnapshotReader.Read(schedule));

    public ScheduleExcelExportResult Export(ViewSchedule schedule, string filePath) =>
        ExportMany(schedule.Document, new[] { schedule }, filePath, editable: false);

    public SheetGrid BuildPreview(ViewSchedule schedule, bool editable, int maxRows) =>
        InReadTransaction(schedule.Document, () => editable
            ? SheetGridBuilder.FromEditable(ScheduleEditableReader.Read(schedule), maxRows)
            : SheetGridBuilder.FromSnapshot(ScheduleSnapshotReader.Read(schedule, mapRows: false, progress: null), maxRows));

    public IReadOnlyList<SheetGrid> ReadWorkbookGrids(string filePath, int maxRows)
    {
        try { return SheetGridBuilder.FromWorkbook(filePath, maxRows); }
        catch (Exception ex)
        {
            _logger.Error(ex, "ScheduleExcel: no se pudo leer la vista previa de '{File}'.", filePath);
            return Array.Empty<SheetGrid>();
        }
    }

    public ScheduleExcelExportResult ExportMany(Document doc, IReadOnlyList<ViewSchedule> schedules, string filePath, bool editable,
        Action<double, string>? progress = null)
    {
        if (schedules is null || schedules.Count == 0)
            return new ScheduleExcelExportResult { Success = false, FilePath = filePath, ErrorMessage = "No hay tablas seleccionadas." };

        string label = schedules.Count == 1 ? schedules[0].Name : schedules.Count + " tablas";
        try
        {
            int rows = 0, cols = 0;
            var skipped = new List<string>();
            using var wb = new XLWorkbook();
            InReadTransaction(doc, () =>
            {
                for (int i = 0; i < schedules.Count; i++)
                {
                    var vs = schedules[i];
                    double from = (double)i / schedules.Count, span = 0.9 / schedules.Count;
                    string what = schedules.Count == 1 ? "Leyendo la tabla" : "Leyendo «" + vs.Name + "» (" + (i + 1) + "/" + schedules.Count + ")";
                    void Report(double f) => progress?.Invoke(from * 0.9 + span * f, what);
                    Report(0);
                    if (editable)
                    {
                        var data = ScheduleEditableReader.Read(vs, Report);
                        ScheduleEditableWorkbook.WriteSheet(wb, data);
                        rows += data.Rows.Count;
                        cols = Math.Max(cols, data.Columns.Count + 1);
                        skipped.AddRange(data.SkippedFields.Select(f => vs.Name + ": " + f));
                    }
                    else
                    {
                        var snap = ScheduleSnapshotReader.Read(vs, mapRows: true, progress: Report);
                        rows += ScheduleExcelWriter.WriteSheet(wb, snap);
                        cols = Math.Max(cols, snap.ColumnCount);
                    }
                }
                return 0;
            });

            // Las pestañas internas al final
            foreach (var name in new[] { ScheduleWorkbookMeta.SheetName, ScheduleEditableWorkbook.ColumnsSheetName, ScheduleRowsSheet.SheetName })
                if (wb.TryGetWorksheet(name, out var ws)) ws.Position = wb.Worksheets.Count;
            progress?.Invoke(0.92, "Guardando el Excel");
            wb.SaveAs(filePath);
            progress?.Invoke(1, "Hecho");

            _logger.Information("ScheduleExcel: {Label} exportada(s) a '{File}' ({Rows} filas, editable={Editable}).",
                label, filePath, rows, editable);
            return new ScheduleExcelExportResult
            {
                Success = true,
                FilePath = filePath,
                ScheduleName = label,
                SheetCount = schedules.Count,
                RowCount = rows,
                ColumnCount = cols,
                SkippedFields = skipped,
            };
        }
        catch (IOException ex)
        {
            _logger.Error(ex, "ScheduleExcel: no se pudo escribir '{File}'.", filePath);
            return Fail(label, filePath,
                "No se pudo guardar el archivo. Si está abierto en Excel, ciérralo y vuelve a intentarlo.\n\n" + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error(ex, "ScheduleExcel: sin permisos en '{File}'.", filePath);
            return Fail(label, filePath, "No hay permisos para escribir en esa carpeta.\n\n" + ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ScheduleExcel: error exportando {Label}.", label);
            return Fail(label, filePath, ex.Message);
        }
    }

    public ScheduleImportPreview PreviewImport(Document doc, string filePath, Action<double, string>? progress = null)
    {
        try
        {
            progress?.Invoke(0.02, "Leyendo el Excel");
            var file = ScheduleEditableWorkbook.Read(filePath);
            progress?.Invoke(0.15, "Comparando con el modelo");
            var preview = ScheduleImporter.Preview(doc, file, f => progress?.Invoke(0.15 + 0.85 * f, "Comparando con el modelo"));
            if (file.PresentationSheets.Count > 0)
                preview.Warnings.Add("Pestañas de una versión anterior, sin datos para importar (vuelve a exportarlas): " + string.Join(", ", file.PresentationSheets) + ".");
            return preview;
        }
        catch (IOException ex)
        {
            return new ScheduleImportPreview
            {
                ErrorMessage = "No se pudo abrir el archivo. Si está abierto en Excel, guárdalo y ciérralo.\n\n" + ex.Message,
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ScheduleExcel: error leyendo '{File}'.", filePath);
            return new ScheduleImportPreview { ErrorMessage = ex.Message };
        }
    }

    public ScheduleImportResult ApplyImport(Document doc, IReadOnlyList<ScheduleCellChange> changes, Action<double, string>? progress = null)
    {
        try
        {
            var r = ScheduleImporter.Apply(doc, changes, f => progress?.Invoke(f, f < 0.95 ? "Escribiendo valores" : "Confirmando en Revit"));
            progress?.Invoke(1, "Hecho");
            _logger.Information("ScheduleExcel: importados {Applied} cambios ({Failed} fallidos).", r.Applied, r.Failed);
            return r;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ScheduleExcel: error aplicando cambios.");
            return new ScheduleImportResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // ====================================================================

    /// <summary>
    /// Leer una tabla (GetTableData, estilos de celda) hace que Revit la
    /// regenere, y eso cuenta como modificacion: se lee dentro de una
    /// transaccion que se revierte.
    /// </summary>
    private static T InReadTransaction<T>(Document doc, Func<T> read)
    {
        if (doc.IsModifiable || doc.IsReadOnly) return read();
        using var tx = new Transaction(doc, "Leer tablas para Excel");
        tx.Start();
        try { return read(); }
        finally { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); }
    }

    private static string CategoryName(Document doc, ViewSchedule s)
    {
        try
        {
            var id = s.Definition?.CategoryId;
            if (id is null || id == ElementId.InvalidElementId) return "Multicategoría";
            return Category.GetCategory(doc, id)?.Name ?? "";
        }
        catch { return ""; }
    }

    private static ScheduleExcelExportResult Fail(string label, string path, string msg) => new()
    {
        Success = false,
        FilePath = path,
        ScheduleName = label,
        ErrorMessage = msg,
    };
}
