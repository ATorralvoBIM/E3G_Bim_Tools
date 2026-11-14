using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Views;

namespace E3G.BimTools.DiTools.Core.Services.Views.Compat;

/// <summary>
/// Implementacion por defecto del adapter View Range, valida para Revit 2025-2027.
/// Para Revit 2022-2024 (.NET Framework 4.8) se inyectara otra implementacion
/// si se detecta divergencia; de momento la API es identica.
///
/// API base: <c>ViewPlan.GetViewRange()</c> devuelve un <c>PlanViewRange</c>,
/// que expone <c>GetLevelId(PlanViewPlane)</c> y <c>GetOffset(PlanViewPlane)</c>.
/// Para escribir: <c>PlanViewRange.SetLevelId</c> + <c>SetOffset</c> y luego
/// <c>ViewPlan.SetViewRange(range)</c> dentro de la transaccion del llamante.
/// </summary>
public sealed class ViewRangeCompat_Default : IViewRangeCompat
{
    public ViewRangeInfo Read(ViewPlan viewPlan)
    {
        var range = viewPlan.GetViewRange();

        return new ViewRangeInfo(
            topLevelId:        range.GetLevelId(PlanViewPlane.TopClipPlane),
            topOffset:         range.GetOffset(PlanViewPlane.TopClipPlane),
            cutPlaneOffset:    range.GetOffset(PlanViewPlane.CutPlane),
            bottomLevelId:     range.GetLevelId(PlanViewPlane.BottomClipPlane),
            bottomOffset:      range.GetOffset(PlanViewPlane.BottomClipPlane),
            viewDepthLevelId:  range.GetLevelId(PlanViewPlane.ViewDepthPlane),
            viewDepthOffset:   range.GetOffset(PlanViewPlane.ViewDepthPlane));
    }

    public void Write(ViewPlan viewPlan, ViewRangeInfo info)
    {
        var range = viewPlan.GetViewRange();

        if (info.TopLevelId is not null)
            range.SetLevelId(PlanViewPlane.TopClipPlane, info.TopLevelId);
        range.SetOffset(PlanViewPlane.TopClipPlane, info.TopOffset);

        range.SetOffset(PlanViewPlane.CutPlane, info.CutPlaneOffset);

        if (info.BottomLevelId is not null)
            range.SetLevelId(PlanViewPlane.BottomClipPlane, info.BottomLevelId);
        range.SetOffset(PlanViewPlane.BottomClipPlane, info.BottomOffset);

        if (info.ViewDepthLevelId is not null)
            range.SetLevelId(PlanViewPlane.ViewDepthPlane, info.ViewDepthLevelId);
        range.SetOffset(PlanViewPlane.ViewDepthPlane, info.ViewDepthOffset);

        viewPlan.SetViewRange(range);
    }
}
