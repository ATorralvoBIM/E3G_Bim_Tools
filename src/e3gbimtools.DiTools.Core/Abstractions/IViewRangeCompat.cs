using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Views;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Capa de compatibilidad para PlanViewRange (Sprint 4).
/// La API de Revit cambio entre 2022 y 2025/2026:
///  - <c>ViewPlan.GetViewRange()</c>/<c>SetViewRange()</c>: estable.
///  - Constantes <c>PlanViewPlane.TopClipPlane</c>, <c>CutPlane</c>, etc. estables.
///  - <c>PlanViewRange.GetOffset()</c>: cambio de override silencioso en 2024.
/// Esta interfaz aisla esas diferencias para que el resto del codigo solo
/// hable en terminos de <see cref="ViewRangeInfo"/>.
/// </summary>
public interface IViewRangeCompat
{
    /// <summary>Lee el View Range de una <c>ViewPlan</c>.</summary>
    ViewRangeInfo Read(ViewPlan viewPlan);

    /// <summary>
    /// Aplica <paramref name="range"/> a la vista plan dentro de una transaccion
    /// que el llamante ya tiene abierta.
    /// </summary>
    void Write(ViewPlan viewPlan, ViewRangeInfo range);
}
