using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Views;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Lectura y edicion masiva de PlanViewRange (Sprint 4).
/// Usa <see cref="IViewRangeCompat"/> internamente para aislar diferencias
/// entre versiones de Revit (2022 / 2025 / 2026).
/// </summary>
public interface IViewRangeService
{
    /// <summary>Lee el View Range de una vista plan o de una plantilla.</summary>
    ViewRangeInfo? GetViewRange(Document doc, ElementId viewOrTemplateId);

    /// <summary>
    /// Aplica el mismo View Range a N vistas en una unica Transaction.
    /// Falla la transaccion completa si <paramref name="range"/> no es consistente.
    /// </summary>
    ViewTemplateApplyResult ApplyViewRange(
        Document doc,
        ViewRangeInfo range,
        IReadOnlyList<ElementId> targetViewIds);

    /// <summary>Validacion sin tocar el modelo (top &gt; cut plane &gt; bottom, etc).</summary>
    bool Validate(ViewRangeInfo range, out string? error);
}
