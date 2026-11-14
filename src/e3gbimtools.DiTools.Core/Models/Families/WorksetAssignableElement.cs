using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Fila de la lista de elementos asignables a un workset desde la pestaña Worksets
/// (Sprint 7 — FamilyReviser). Limitada a familias y vínculos (Revit/CAD).
/// </summary>
public sealed class WorksetAssignableElement
{
    /// <summary>Id del elemento que se reasignará (familia o instancia de vínculo).</summary>
    public ElementId Id { get; set; } = ElementId.InvalidElementId;

    /// <summary>Tipo de elemento: "Familia", "Vínculo Revit" o "Vínculo CAD".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Nombre legible del elemento.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Categoría de Revit del elemento (p. ej. "Mobiliario", "Tuberías").</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Tipo de sistema MEP del elemento, si lo tuviera (en blanco si no aplica).</summary>
    public string SystemType { get; set; } = string.Empty;

    /// <summary>Nombre del workset al que pertenece actualmente.</summary>
    public string CurrentWorkset { get; set; } = string.Empty;

    /// <summary>Si <c>true</c> la fila está marcada en la UI para reasignar.</summary>
    public bool IsSelected { get; set; }
}
