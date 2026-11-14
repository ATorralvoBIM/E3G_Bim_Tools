using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Petición de asignación masiva de elementos a un workset (Sprint 7 — FamilyReviser).
/// El scope puede ser una lista de ElementIds explícita, una categoría o ambas.
/// </summary>
public sealed class WorksetAssignRequest
{
    /// <summary>Workset de destino.</summary>
    public WorksetId TargetWorksetId { get; set; } = WorksetId.InvalidWorksetId;

    /// <summary>
    /// ElementIds explícitos a reasignar.
    /// Si está vacío y <see cref="CategoryFilter"/> también, se usa todo el documento.
    /// </summary>
    public IList<ElementId> ElementIds { get; set; } = new List<ElementId>();

    /// <summary>
    /// Categorías por las que filtrar. Si no está vacía, se reasignan todos los
    /// elementos de esas categorías (intersecado con <see cref="ElementIds"/> si ambos tienen valores).
    /// </summary>
    public IList<BuiltInCategory> CategoryFilter { get; set; } = new List<BuiltInCategory>();

    /// <summary>
    /// Si se especificó un FilterDefinitionId (GUID de una plantilla de OneFilter)
    /// se usa como scope adicional. Null = no se usa.
    /// </summary>
    public System.Guid? SavedFilterId { get; set; }

    /// <summary>
    /// Si <c>true</c>, incluye en el scope todas las instancias de importación/vínculo CAD
    /// (elementos <c>ImportInstance</c>), que no se pueden filtrar limpiamente por categoría.
    /// </summary>
    public bool IncludeImportInstances { get; set; }

    public bool IsValid(out string? error)
    {
        if (TargetWorksetId == WorksetId.InvalidWorksetId)
        {
            error = "No se ha especificado el workset de destino.";
            return false;
        }
        error = null;
        return true;
    }
}
