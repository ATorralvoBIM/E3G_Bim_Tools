using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Views;

namespace E3G.BimTools.DiTools.Core.Services.Views;

/// <summary>
/// Lectura/escritura masiva de PlanViewRange (Sprint 4).
/// Delega la conversion API por version de Revit en <see cref="IViewRangeCompat"/>.
/// </summary>
public sealed class ViewRangeService : IViewRangeService
{
    private readonly ITransactionService _tx;
    private readonly IViewRangeCompat _compat;
    private readonly ILogger _logger;

    public ViewRangeService(
        ITransactionService tx,
        IViewRangeCompat compat,
        ILoggerFactory loggerFactory)
    {
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
        _compat = compat ?? throw new ArgumentNullException(nameof(compat));
        _logger = loggerFactory?.CreateLogger<ViewRangeService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public ViewRangeInfo? GetViewRange(Document doc, ElementId viewOrTemplateId)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (doc.GetElement(viewOrTemplateId) is not ViewPlan vp)
            return null;
        return _compat.Read(vp);
    }

    public bool Validate(ViewRangeInfo range, out string? error)
    {
        if (range is null)
        {
            error = "View range nulo.";
            return false;
        }
        if (!range.IsConsistent)
        {
            error = "View Range invalido: requiere top > cut plane > bottom y view depth <= bottom.";
            return false;
        }
        error = null;
        return true;
    }

    public ViewTemplateApplyResult ApplyViewRange(
        Document doc,
        ViewRangeInfo range,
        IReadOnlyList<ElementId> targetViewIds)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (range is null) throw new ArgumentNullException(nameof(range));
        if (targetViewIds is null || targetViewIds.Count == 0)
            return new ViewTemplateApplyResult { Success = false, Summary = "Selecciona vistas destino." };

        if (!Validate(range, out var validationError))
            return new ViewTemplateApplyResult { Success = false, Summary = validationError ?? "View Range invalido." };

        var result = new ViewTemplateApplyResult();
        _tx.Run(doc, $"ViewManager: aplicar View Range a {targetViewIds.Count} vistas", d =>
        {
            foreach (var id in targetViewIds)
            {
                if (d.GetElement(id) is not ViewPlan vp)
                {
                    result.SkippedCount++;
                    continue;
                }
                try
                {
                    _compat.Write(vp, range);
                    result.AppliedCount++;
                }
                catch (Exception ex)
                {
                    _logger.Warning("Fallo al aplicar View Range a {0}: {1}", id, ex.Message);
                    result.FailedCount++;
                    result.FailedViewIds.Add(id);
                    result.Warnings.Add($"Vista {id}: {ex.Message}");
                }
            }
        });

        result.Success = result.FailedCount == 0;
        result.Summary = $"View Range: {result.AppliedCount} aplicadas, " +
                         $"{result.SkippedCount} omitidas, {result.FailedCount} con error.";
        return result;
    }
}
