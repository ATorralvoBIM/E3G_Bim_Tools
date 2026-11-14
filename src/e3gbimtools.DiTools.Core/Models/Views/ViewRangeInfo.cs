using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Views;

/// <summary>
/// Snapshot del PlanViewRange de una vista (Sprint 4 - ViewManager).
/// Cada plano (top, cut plane, bottom, view depth) se modela como par
/// (LevelId, Offset).  Las distancias estan en pies internos de Revit.
/// </summary>
public sealed class ViewRangeInfo
{
    public ViewRangeInfo(
        ElementId? topLevelId, double topOffset,
        double cutPlaneOffset,
        ElementId? bottomLevelId, double bottomOffset,
        ElementId? viewDepthLevelId, double viewDepthOffset)
    {
        TopLevelId = topLevelId;
        TopOffset = topOffset;
        CutPlaneOffset = cutPlaneOffset;
        BottomLevelId = bottomLevelId;
        BottomOffset = bottomOffset;
        ViewDepthLevelId = viewDepthLevelId;
        ViewDepthOffset = viewDepthOffset;
    }

    public ElementId? TopLevelId { get; }
    public double TopOffset { get; }

    /// <summary>Offset del cut plane respecto al nivel de la vista (interno: pies).</summary>
    public double CutPlaneOffset { get; }

    public ElementId? BottomLevelId { get; }
    public double BottomOffset { get; }

    public ElementId? ViewDepthLevelId { get; }
    public double ViewDepthOffset { get; }

    /// <summary>
    /// Validacion barata: top &gt; cut plane &gt; bottom y view depth &le; bottom.
    /// No comprueba niveles distintos (eso es responsabilidad del Compat).
    /// </summary>
    public bool IsConsistent =>
        TopOffset > CutPlaneOffset &&
        CutPlaneOffset > BottomOffset &&
        ViewDepthOffset <= BottomOffset;
}
