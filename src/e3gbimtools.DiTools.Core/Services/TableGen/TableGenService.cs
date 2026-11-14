using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.TableGen;

namespace E3G.BimTools.DiTools.Core.Services.TableGen;

/// <summary>
/// Implementacion de <see cref="ITableGenService"/>.
/// Sprint 8 (P1): Excel → Drafting View.
/// Sprint 9 (P2): lote, sobreescritura, colocar en hoja, Schedule → DV.
/// </summary>
public sealed class TableGenService : ITableGenService
{
    private readonly ExcelTableReader      _reader         = new();
    private readonly DraftingViewGenerator _generator      = new();
    private readonly ScheduleTableReader   _scheduleReader = new();
    private readonly ILogger               _logger;

    public TableGenService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<TableGenService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // ── Sprint 8 (P1) ───────────────────────────────────────────────────

    public IReadOnlyList<string> GetSheetNames(string filePath)
    {
        try   { return _reader.GetSheetNames(filePath); }
        catch (Exception ex)
        {
            _logger.Error(ex, "TableGenService.GetSheetNames: error en '{FilePath}'.", filePath);
            throw;
        }
    }

    public string GetUsedRangeAddress(string filePath, string sheetName)
    {
        try   { return _reader.GetUsedRangeAddress(filePath, sheetName); }
        catch (Exception ex)
        {
            _logger.Error(ex,
                "TableGenService.GetUsedRangeAddress: error en '{FilePath}' hoja '{Sheet}'.",
                filePath, sheetName);
            throw;
        }
    }

    public ExcelSheetData ReadPreview(TableGenRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        _logger.Information(
            "TableGenService.ReadPreview: '{FilePath}' / '{Sheet}' / '{Range}'.",
            request.FilePath, request.SheetName, request.RangeAddress ?? "(todo)");
        return _reader.Read(request);
    }

    public TableGenResult Generate(Document doc, TableGenRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        _logger.Information(
            "TableGenService.Generate: inicio '{FilePath}' / '{Sheet}' / '{Range}'.",
            request.FilePath, request.SheetName, request.RangeAddress ?? "(todo)");

        // Si OverwriteExisting, borrar vista preexistente antes de generar.
        if (request.OverwriteExisting && !string.IsNullOrWhiteSpace(request.ViewName))
            DeleteViewIfExists(doc, request.ViewName);

        ExcelSheetData data;
        try   { data = _reader.Read(request); }
        catch (Exception ex)
        {
            _logger.Error(ex, "TableGenService.Generate: error leyendo Excel.");
            return new TableGenResult
            {
                Success      = false,
                ErrorMessage = $"Error leyendo el Excel: {ex.Message}",
            };
        }

        _logger.Information(
            "TableGenService.Generate: {Cells} celdas ({Rows}x{Cols}).",
            data.Cells.Count, data.RowCount, data.ColumnCount);

        var warnings = new List<string>();
        var result   = _generator.Generate(doc, data, request, warnings);

        if (result.Success)
            _logger.Information(
                "TableGenService.Generate: vista '{Name}' creada en {Ms} ms.",
                result.ViewName ?? "(sin nombre)", result.ElapsedMs);
        else
            _logger.Error(null,
                "TableGenService.Generate: fallo: {Error}", result.ErrorMessage ?? "(sin detalle)");

        return result;
    }

    // ── Sprint 9 (P2) — Consultas ────────────────────────────────────────

    public IReadOnlyList<ScheduleInfo> GetSchedules(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        return _scheduleReader.GetSchedules(doc);
    }

    public IReadOnlyList<SheetPickerInfo> GetDrawingSheets(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .OrderBy(s => s.SheetNumber)
            .Select(s => new SheetPickerInfo
            {
                Id          = s.Id,
                SheetNumber = s.SheetNumber,
                SheetName   = s.Name,
            })
            .ToList();
    }

    public IReadOnlyList<DraftingViewInfo> GetDraftingViews(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        // Pre-index de vistas ya colocadas en alguna hoja.
        var placedViewIds = new HashSet<ElementId>(
            new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Select(vp => vp.ViewId));

        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewDrafting))
            .Cast<ViewDrafting>()
            .Where(v => !v.IsTemplate)
            .OrderBy(v => v.Name)
            .Select(v => new DraftingViewInfo
            {
                Id             = v.Id,
                Name           = v.Name,
                IsPlacedOnSheet = placedViewIds.Contains(v.Id),
            })
            .ToList();
    }

    // ── Sprint 9 (P2) — Operaciones ──────────────────────────────────────

    public BatchTableGenResult GenerateBatch(Document doc, BatchTableGenRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        _logger.Information(
            "TableGenService.GenerateBatch: inicio lote '{FilePath}'.", request.FilePath);

        var sw     = Stopwatch.StartNew();
        IReadOnlyList<string> sheetNames;

        try   { sheetNames = _reader.GetSheetNames(request.FilePath); }
        catch (Exception ex)
        {
            _logger.Error(ex, "TableGenService.GenerateBatch: no se pudo leer el Excel.");
            return new BatchTableGenResult
            {
                Results      = new[]
                {
                    new TableGenResult
                    {
                        Success      = false,
                        ErrorMessage = $"No se pudo leer el archivo: {ex.Message}",
                    }
                },
                FailCount    = 1,
                TotalElapsedMs = sw.ElapsedMilliseconds,
            };
        }

        var results     = new List<TableGenResult>(sheetNames.Count);
        int successCount = 0;
        int failCount    = 0;

        string fileBase = Path.GetFileNameWithoutExtension(request.FilePath);

        foreach (var sheet in sheetNames)
        {
            string viewName = BuildBatchViewName(request.NamingPattern, fileBase, sheet);

            var req = new TableGenRequest
            {
                FilePath        = request.FilePath,
                SheetName       = sheet,
                ViewName        = viewName,
                MmToFeet        = request.MmToFeet,
                OverwriteExisting = request.OverwriteExisting,
            };

            var r = Generate(doc, req);
            results.Add(r);
            if (r.Success) successCount++; else failCount++;
        }

        sw.Stop();
        _logger.Information(
            "TableGenService.GenerateBatch: lote completado. {Ok} OK, {Fail} errores en {Ms} ms.",
            successCount, failCount, sw.ElapsedMilliseconds);

        return new BatchTableGenResult
        {
            Results        = results,
            SuccessCount   = successCount,
            FailCount      = failCount,
            TotalElapsedMs = sw.ElapsedMilliseconds,
        };
    }

    public PlaceOnSheetResult PlaceOnSheet(Document doc, PlaceOnSheetRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (request.DraftingViewId == ElementId.InvalidElementId ||
            request.SheetId        == ElementId.InvalidElementId)
        {
            return new PlaceOnSheetResult
            {
                Success      = false,
                ErrorMessage = "DraftingViewId o SheetId no son validos.",
            };
        }

        var warnings = new List<string>();

        try
        {
            var sheet = doc.GetElement(request.SheetId) as ViewSheet;
            if (sheet is null)
                return new PlaceOnSheetResult
                {
                    Success      = false,
                    ErrorMessage = $"No se encontro el Drawing Sheet con Id {request.SheetId}.",
                };

            // Si la vista ya tiene viewport en esta hoja, eliminarlo primero.
            var existingVp = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .FirstOrDefault(vp => vp.ViewId == request.DraftingViewId
                                    && vp.SheetId == request.SheetId);

            ElementId viewportId;

            using (var tx = new Transaction(doc, "TableGen — Colocar vista en hoja"))
            {
                tx.Start();

                if (existingVp is not null)
                {
                    doc.Delete(existingVp.Id);
                    warnings.Add("Viewport existente eliminado antes de recolocar.");
                }

                // Centro del cajetín + offset del usuario.
                // GetTitleBlockOrigin() no existe en todas las versiones;
                // usamos BoundingBox del sheet en coordenadas de papel (feet).
                var bb      = sheet.get_BoundingBox(null);
                double cx   = bb is not null ? (bb.Min.X + bb.Max.X) / 2.0 : 0.0;
                double cy   = bb is not null ? (bb.Min.Y + bb.Max.Y) / 2.0 : 0.0;

                double xFt  = cx + request.XOffsetMm / 304.8;
                double yFt  = cy + request.YOffsetMm / 304.8;
                var pos     = new XYZ(xFt, yFt, 0);

                var vp = Viewport.Create(doc, request.SheetId, request.DraftingViewId, pos);
                viewportId = vp.Id;

                tx.Commit();
            }

            _logger.Information(
                "TableGenService.PlaceOnSheet: vista {VId} colocada en hoja {SId}.",
                request.DraftingViewId, request.SheetId);

            return new PlaceOnSheetResult
            {
                Success    = true,
                ViewportId = viewportId,
                Warnings   = warnings,
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "TableGenService.PlaceOnSheet: error al colocar vista.");
            return new PlaceOnSheetResult
            {
                Success      = false,
                ErrorMessage = ex.Message,
                Warnings     = warnings,
            };
        }
    }

    public ScheduleExportResult ExportScheduleToView(Document doc, ScheduleToViewRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var sw = Stopwatch.StartNew();

        _logger.Information(
            "TableGenService.ExportScheduleToView: ScheduleId={Id}.", request.ScheduleId);

        ViewSchedule? schedule;
        try
        {
            schedule = doc.GetElement(request.ScheduleId) as ViewSchedule;
            if (schedule is null)
            {
                sw.Stop();
                return new ScheduleExportResult
                {
                    Success      = false,
                    ErrorMessage = $"No se encontro ningun ViewSchedule con Id {request.ScheduleId}.",
                    ElapsedMs    = sw.ElapsedMilliseconds,
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ScheduleExportResult
            {
                Success      = false,
                ErrorMessage = $"Error accediendo al schedule: {ex.Message}",
                ElapsedMs    = sw.ElapsedMilliseconds,
            };
        }

        // Nombre de la vista destino.
        string viewName = !string.IsNullOrWhiteSpace(request.ViewName)
            ? request.ViewName
            : $"TableGen_{schedule.Name}";

        if (request.OverwriteExisting)
            DeleteViewIfExists(doc, viewName);

        // Leer el schedule como ExcelSheetData.
        ExcelSheetData data;
        try
        {
            data = _scheduleReader.Read(schedule, request);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error(ex, "TableGenService.ExportScheduleToView: error leyendo schedule.");
            return new ScheduleExportResult
            {
                Success      = false,
                ErrorMessage = $"Error leyendo el schedule: {ex.Message}",
                ElapsedMs    = sw.ElapsedMilliseconds,
            };
        }

        // Generar la Drafting View reutilizando el generador P1.
        var tableReq = new TableGenRequest
        {
            FilePath        = schedule.Name,   // sin uso real en el generador (solo log)
            SheetName       = schedule.Name,
            ViewName        = viewName,
            MmToFeet        = request.MmToFeet,
            OverwriteExisting = false,         // ya lo gestionamos arriba
        };

        var warnings = new List<string>();
        var genResult = _generator.Generate(doc, data, tableReq, warnings);

        sw.Stop();

        if (genResult.Success)
        {
            _logger.Information(
                "TableGenService.ExportScheduleToView: '{Name}' creada en {Ms} ms.",
                genResult.ViewName ?? string.Empty, sw.ElapsedMilliseconds);
        }

        return new ScheduleExportResult
        {
            Success        = genResult.Success,
            DraftingViewId = genResult.DraftingViewId,
            ViewName       = genResult.ViewName,
            RowCount       = data.RowCount,
            ColumnCount    = data.ColumnCount,
            Warnings       = warnings,
            ElapsedMs      = sw.ElapsedMilliseconds,
            ErrorMessage   = genResult.ErrorMessage,
        };
    }

    // ── Helpers privados ─────────────────────────────────────────────────

    private static string BuildBatchViewName(string? pattern, string fileBase, string sheetName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return $"TableGen_{fileBase}_{sheetName}";

        return pattern
            .Replace("{sheet}", sheetName,     StringComparison.OrdinalIgnoreCase)
            .Replace("{file}",  fileBase,      StringComparison.OrdinalIgnoreCase)
            .Replace("{Sheet}", sheetName)
            .Replace("{File}",  fileBase);
    }

    /// <summary>
    /// Elimina la primera Drafting View cuyo nombre coincida exactamente.
    /// Si la vista tiene viewports en hojas, los elimina primero (Revit lo exige).
    /// </summary>
    private static void DeleteViewIfExists(Document doc, string viewName)
    {
        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewDrafting))
            .Cast<ViewDrafting>()
            .FirstOrDefault(v => string.Equals(v.Name, viewName,
                                               StringComparison.OrdinalIgnoreCase));

        if (existing is null) return;

        // Recopilar viewports que referencian esta vista.
        var viewportIds = new FilteredElementCollector(doc)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .Where(vp => vp.ViewId == existing.Id)
            .Select(vp => vp.Id)
            .ToList();

        using var tx = new Transaction(doc, "TableGen — Eliminar vista preexistente");
        tx.Start();
        foreach (var vpId in viewportIds)
            doc.Delete(vpId);
        doc.Delete(existing.Id);
        tx.Commit();
    }
}
