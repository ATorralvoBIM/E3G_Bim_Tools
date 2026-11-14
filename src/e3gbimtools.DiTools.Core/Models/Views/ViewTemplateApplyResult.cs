using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Views;

/// <summary>
/// Resultado de aplicar una View Template a N vistas en lote (Sprint 4).
/// Sigue el mismo patron que <c>ParaManagerActionResult</c>.
/// </summary>
public sealed class ViewTemplateApplyResult
{
    public bool Success { get; set; }
    public int AppliedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public IList<string> Warnings { get; set; } = new List<string>();
    public IList<ElementId> FailedViewIds { get; set; } = new List<ElementId>();

    public string ToSummary() => string.IsNullOrEmpty(Summary)
        ? $"{AppliedCount} aplicadas, {SkippedCount} omitidas, {FailedCount} con error."
        : Summary;
}
