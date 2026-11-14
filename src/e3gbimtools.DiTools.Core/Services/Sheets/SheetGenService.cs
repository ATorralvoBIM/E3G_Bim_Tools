using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sheets;

namespace E3G.BimTools.DiTools.Core.Services.Sheets;

/// <summary>
/// Implementación principal de ISheetGenService (Sprint 6 — SheetGen).
///
/// Patrón de transacciones:
///   - BulkCreate / ImportFromExcel → una sola Transaction para todas las sheets.
///   - ApplyRevisions               → una sola Transaction.
///   - RenumberSheets               → una sola Transaction.
///   - ExportToExcel, Audit, Get*   → solo lectura, sin transacción.
/// </summary>
public sealed class SheetGenService : ISheetGenService
{
    private readonly ITransactionService    _tx;
    private readonly IExcelService          _excel;
    private readonly SheetLayoutRepository  _layouts;
    private readonly ILogger                _logger;

    public SheetGenService(
        ITransactionService   tx,
        IExcelService         excel,
        SheetLayoutRepository layoutRepository,
        ILoggerFactory        loggerFactory)
    {
        _tx      = tx      ?? throw new ArgumentNullException(nameof(tx));
        _excel   = excel   ?? throw new ArgumentNullException(nameof(excel));
        _layouts = layoutRepository ?? throw new ArgumentNullException(nameof(layoutRepository));
        _logger  = loggerFactory?.CreateLogger<SheetGenService>()
                   ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // ================================================================== //
    //  Creación / Import / Export                                          //
    // ================================================================== //

    public Task<SheetGenResult> BulkCreateAsync(
        Document doc,
        IList<SheetDescriptor> descriptors,
        SheetLayoutDefinition? layout,
        IProgress<int>? progress)
    {
        if (doc is null)         throw new ArgumentNullException(nameof(doc));
        if (descriptors is null) throw new ArgumentNullException(nameof(descriptors));

        // Ejecutamos síncronamente dentro de Task para no bloquear el hilo
        // de UI (Revit no es thread-safe; la transacción se abre en este hilo).
        return Task.FromResult(CreateSheets(doc, descriptors, layout, progress));
    }

    public SheetGenResult ImportFromExcel(
        Document doc,
        string filePath,
        SheetLayoutDefinition? layout)
    {
        if (doc      is null) throw new ArgumentNullException(nameof(doc));
        if (filePath is null) throw new ArgumentNullException(nameof(filePath));

        IList<SheetDescriptor> descriptors;
        try
        {
            descriptors = _excel.ReadSheet<SheetDescriptor>(filePath, "Sheets").ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "SheetGenService.ImportFromExcel: error leyendo '{FilePath}'.", filePath);
            return new SheetGenResult { SkippedNumbers = { $"Error al leer Excel: {ex.Message}" } };
        }

        return CreateSheets(doc, descriptors, layout, progress: null);
    }

    public void ExportToExcel(Document doc, string filePath)
    {
        if (doc      is null) throw new ArgumentNullException(nameof(doc));
        if (filePath is null) throw new ArgumentNullException(nameof(filePath));

        var sheets = GetAllSheets(doc);
        _excel.WriteSheet(filePath, "Sheets", sheets);
        _logger.Information("SheetGenService.ExportToExcel: {Count} sheets exportadas a '{FilePath}'.", sheets.Count, filePath);
    }

    // ================================================================== //
    //  Revisiones                                                          //
    // ================================================================== //

    public void ApplyRevisions(Document doc, SheetRevisionRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (!request.IsValid(out var error))
        {
            _logger.Warning("SheetGenService.ApplyRevisions: request inválido — {Error}.", error ?? string.Empty);
            return;
        }

        var action = request.Add ? "asignar" : "quitar";
        _tx.Run(doc, $"SheetGen: {action} revisión a {request.SheetIds.Count} sheet(s)", d =>
        {
            foreach (var sheetId in request.SheetIds)
            {
                if (d.GetElement(sheetId) is not ViewSheet sheet) continue;

                var revIds = sheet.GetAdditionalRevisionIds().ToList();

                if (request.Add)
                {
                    if (!revIds.Contains(request.RevisionId))
                        revIds.Add(request.RevisionId);
                }
                else
                {
                    revIds.Remove(request.RevisionId);
                }

                sheet.SetAdditionalRevisionIds(revIds);
            }
        });

        _logger.Information("SheetGenService.ApplyRevisions: revisión {Action}ada a {Count} sheet(s).", action, request.SheetIds.Count);
    }

    // ================================================================== //
    //  Renumeración                                                        //
    // ================================================================== //

    public void RenumberSheets(Document doc, SheetRenumberRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (!request.IsValid(out var error))
        {
            _logger.Warning("SheetGenService.RenumberSheets: request inválido — {Error}.", error ?? string.Empty);
            return;
        }

        _tx.Run(doc, $"SheetGen: renumerar {request.SheetIds.Count} sheet(s)", d =>
        {
            for (int i = 0; i < request.SheetIds.Count; i++)
            {
                if (d.GetElement(request.SheetIds[i]) is not ViewSheet sheet) continue;
                var newNumber = request.ComputeNumber(i);
                sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.Set(newNumber);
            }
        });

        _logger.Information("SheetGenService.RenumberSheets: {Count} sheet(s) renumeradas.", request.SheetIds.Count);
    }

    public IList<(ElementId SheetId, string OldNumber, string NewNumber)> PreviewRenumber(
        Document doc,
        SheetRenumberRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var result = new List<(ElementId, string, string)>();
        for (int i = 0; i < request.SheetIds.Count; i++)
        {
            if (doc.GetElement(request.SheetIds[i]) is not ViewSheet sheet) continue;
            var oldNumber = sheet.SheetNumber;
            var newNumber = request.ComputeNumber(i);
            result.Add((request.SheetIds[i], oldNumber, newNumber));
        }
        return result;
    }

    // ================================================================== //
    //  Auditoría                                                           //
    // ================================================================== //

    public SheetAuditResult Audit(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var allSheets = GetAllSheets(doc);
        var result    = new SheetAuditResult();

        // Huérfanas
        foreach (var s in allSheets)
            if (s.ViewCount == 0)
                result.OrphanSheets.Add(s);

        // Duplicados
        var duplicates = allSheets
            .GroupBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var dup in duplicates)
            result.DuplicateNumbers.Add(dup);

        _logger.Information(
            "SheetGenService.Audit: {Orphans} huérfanas, {Duplicates} duplicados.",
            result.OrphanSheets.Count, result.DuplicateNumbers.Count);

        return result;
    }

    public IList<SheetInfo> GetAllSheets(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(vs => !vs.IsTemplate)
            .Select(vs => new SheetInfo
            {
                ElementId   = vs.Id,
                SheetNumber = vs.SheetNumber,
                SheetName   = vs.Name,
                ViewCount   = vs.GetAllViewports().Count,
                // SHEET_DISCIPLINE no existe en todas las versiones de la API;
                // usar LookupParameter por nombre como fallback robusto.
                Discipline  = (vs.LookupParameter("Sheet Discipline")
                            ?? vs.LookupParameter("Discipline"))?.AsString() ?? string.Empty
            })
            .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ================================================================== //
    //  Layouts                                                             //
    // ================================================================== //

    public IList<SheetLayoutDefinition> GetLayouts()          => _layouts.GetAll();
    public void SaveLayout(SheetLayoutDefinition layout)       => _layouts.Save(layout);
    public void DeleteLayout(Guid layoutId)                    => _layouts.Delete(layoutId);

    // ================================================================== //
    //  Privado                                                             //
    // ================================================================== //

    private SheetGenResult CreateSheets(
        Document doc,
        IList<SheetDescriptor> descriptors,
        SheetLayoutDefinition? layout,
        IProgress<int>? progress)
    {
        var result = new SheetGenResult();
        var sw     = Stopwatch.StartNew();

        if (descriptors.Count == 0)
        {
            result.Warnings.Add("No hay descriptores que procesar.");
            return result;
        }

        // --- Validación previa (sin transacción) ---
        var existingNumbers = GetAllSheets(doc)
            .Select(s => s.SheetNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var validDescriptors = new List<SheetDescriptor>();
        foreach (var d in descriptors)
        {
            if (!d.IsValid(out var err))
            {
                result.SkippedNumbers.Add($"[inválido] {err}");
                continue;
            }
            if (existingNumbers.Contains(d.SheetNumber))
            {
                result.SkippedNumbers.Add($"[duplicado] {d.SheetNumber} — ya existe en el documento.");
                continue;
            }
            existingNumbers.Add(d.SheetNumber); // reservar para batch
            validDescriptors.Add(d);
        }

        if (validDescriptors.Count == 0)
        {
            result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }

        // --- Resolución de title blocks (sin transacción) ---
        var titleBlockTypes = ResolveTitleBlockTypes(doc);

        // --- Transacción única ---
        _tx.Run(doc, $"SheetGen: crear {validDescriptors.Count} sheet(s)", txDoc =>
        {
            for (int i = 0; i < validDescriptors.Count; i++)
            {
                var desc = validDescriptors[i];

                if (!titleBlockTypes.TryGetValue(desc.TitleBlockFamilyTypeName, out var tbTypeId))
                {
                    result.SkippedNumbers.Add(
                        $"[sin title block] {desc.SheetNumber} — no se encontró '{desc.TitleBlockFamilyTypeName}'.");
                    continue;
                }

                ViewSheet sheet;
                try
                {
                    sheet = ViewSheet.Create(txDoc, tbTypeId);
                }
                catch (Exception ex)
                {
                    result.SkippedNumbers.Add($"[error] {desc.SheetNumber} — {ex.Message}");
                    continue;
                }

                sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.Set(desc.SheetNumber);
                sheet.get_Parameter(BuiltInParameter.SHEET_NAME)?.Set(desc.SheetName);

                if (!string.IsNullOrWhiteSpace(desc.Discipline))
                    (sheet.LookupParameter("Sheet Discipline")
                  ?? sheet.LookupParameter("Discipline"))?.Set(desc.Discipline);

                result.CreatedSheetIds.Add(sheet.Id);

                // Colocar vistas según layout
                if (layout is not null)
                    ApplyLayoutToSheet(txDoc, sheet, layout, result.Warnings);

                progress?.Report((i + 1) * 100 / validDescriptors.Count);
            }
        });

        sw.Stop();
        result.ElapsedMs = sw.ElapsedMilliseconds;

        _logger.Information(
            "SheetGenService.CreateSheets: {Created} creadas, {Skipped} omitidas, {Ms} ms.",
            result.CreatedSheetIds.Count, result.SkippedNumbers.Count, result.ElapsedMs);

        return result;
    }

    private static void ApplyLayoutToSheet(
        Document doc,
        ViewSheet sheet,
        SheetLayoutDefinition layout,
        IList<string> warnings)
    {
        foreach (var spec in layout.Placements)
        {
            // Buscar la vista por nombre
            var view = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v =>
                    string.Equals(v.Name, spec.ViewName, StringComparison.OrdinalIgnoreCase) &&
                    !v.IsTemplate &&
                    v.ViewType != ViewType.Schedule);

            if (view is null)
            {
                warnings.Add($"Layout '{layout.Name}': vista '{spec.ViewName}' no encontrada.");
                continue;
            }

            // Comprobar que la vista no está ya en otra sheet
            if (view.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER)?.AsString() is { Length: > 0 } existingSheet)
            {
                warnings.Add(
                    $"Layout '{layout.Name}': vista '{spec.ViewName}' ya está en la sheet '{existingSheet}', omitida.");
                continue;
            }

            // Convertir mm → pies (unidades internas de Revit)
            double xFeet = UnitUtils.ConvertToInternalUnits(spec.XOffsetMm, UnitTypeId.Millimeters);
            double yFeet = UnitUtils.ConvertToInternalUnits(spec.YOffsetMm, UnitTypeId.Millimeters);

            try
            {
                // Aplicar escala antes de colocar para que el viewport la herede
                if (spec.ScaleOverride.HasValue && view.get_Parameter(BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC) is not null)
                    view.Scale = spec.ScaleOverride.Value;

                Viewport.Create(doc, sheet.Id, view.Id, new XYZ(xFeet, yFeet, 0));
            }
            catch (Exception ex)
            {
                warnings.Add($"Layout '{layout.Name}': no se pudo colocar '{spec.ViewName}': {ex.Message}");
            }
        }
    }

    private static Dictionary<string, ElementId> ResolveTitleBlockTypes(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .Cast<FamilySymbol>()
            .ToDictionary(
                fs => $"{fs.Family.Name} : {fs.Name}",
                fs => fs.Id,
                StringComparer.OrdinalIgnoreCase);
    }
}
