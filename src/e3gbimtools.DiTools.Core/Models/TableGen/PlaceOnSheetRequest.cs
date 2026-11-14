using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Peticion para colocar una Drafting View existente sobre un Drawing Sheet
/// (Sprint 9 — TableGen P2).
/// </summary>
public sealed class PlaceOnSheetRequest
{
    /// <summary>Id de la Drafting View a colocar.</summary>
    public ElementId DraftingViewId { get; init; } = ElementId.InvalidElementId;

    /// <summary>Id del Drawing Sheet (ViewSheet) donde se colocara la vista.</summary>
    public ElementId SheetId { get; init; } = ElementId.InvalidElementId;

    /// <summary>
    /// Desplazamiento X en mm desde el centro del cajetín.
    /// Positivo = hacia la derecha.
    /// </summary>
    public double XOffsetMm { get; init; } = 0.0;

    /// <summary>
    /// Desplazamiento Y en mm desde el centro del cajetín.
    /// Positivo = hacia arriba.
    /// </summary>
    public double YOffsetMm { get; init; } = 0.0;
}
