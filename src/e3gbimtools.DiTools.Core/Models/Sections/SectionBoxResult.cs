using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Sections;

/// <summary>
/// Resultado de aplicar un SectionBoxer (Sprint 5).  Sigue el mismo patron
/// que <c>ViewTemplateApplyResult</c>.
/// </summary>
public sealed class SectionBoxResult
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public IList<string> Warnings { get; set; } = new List<string>();

    /// <summary>Vista 3D donde se aplico el section box (existente o dedicada).</summary>
    public ElementId? ViewId { get; set; }

    /// <summary>Bounding box aplicado, en coordenadas internas (pies).  Null si fallo.</summary>
    public BoundingBoxXYZ? AppliedBox { get; set; }

    /// <summary>True si se creo una vista 3D dedicada en esta operacion.</summary>
    public bool CreatedDedicatedView { get; set; }

    public string ToSummary() => string.IsNullOrEmpty(Summary)
        ? (Success ? "Section box aplicado." : "No se pudo aplicar el section box.")
        : Summary;
}
