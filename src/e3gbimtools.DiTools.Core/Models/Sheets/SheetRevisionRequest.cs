using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Sheets;

/// <summary>
/// Petición para asignar o quitar una revisión a un conjunto de sheets
/// (Sprint 6 — SheetGen).
/// </summary>
public sealed class SheetRevisionRequest
{
    /// <summary>Ids de las sheets a modificar.</summary>
    public IList<ElementId> SheetIds { get; set; } = new List<ElementId>();

    /// <summary>Id de la revisión (elemento Revision en el documento).</summary>
    public ElementId RevisionId { get; set; } = ElementId.InvalidElementId;

    /// <summary>
    /// True = asignar la revisión a las sheets.
    /// False = quitar la revisión de las sheets.
    /// </summary>
    public bool Add { get; set; } = true;

    public bool IsValid(out string? error)
    {
        if (SheetIds.Count == 0)
        {
            error = "Selecciona al menos una sheet.";
            return false;
        }
        if (RevisionId == ElementId.InvalidElementId)
        {
            error = "Selecciona una revisión válida.";
            return false;
        }
        error = null;
        return true;
    }
}
