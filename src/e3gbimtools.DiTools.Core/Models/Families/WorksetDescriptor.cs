using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Describe un workset del documento (Sprint 7 — FamilyReviser).
/// </summary>
public sealed class WorksetDescriptor
{
    /// <summary>Id del workset.</summary>
    public WorksetId Id { get; set; } = new WorksetId(WorksetId.InvalidWorksetId.IntegerValue);

    /// <summary>Nombre del workset.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Nombre propuesto (tras edición en la UI).</summary>
    public string ProposedName { get; set; } = string.Empty;

    /// <summary>Número de elementos que pertenecen a este workset.</summary>
    public int ElementCount { get; set; }

    /// <summary>Si <c>true</c> este workset está seleccionado en la UI.</summary>
    public bool IsSelected { get; set; }

    public bool HasRenameChange =>
        !string.IsNullOrEmpty(ProposedName) && ProposedName != Name;
}
