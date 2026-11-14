using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Describe una familia cargada en el documento (Sprint 7 — FamilyReviser).
/// Agrupa nombre, categoría, número de tipos y el ElementId de la familia.
/// </summary>
public sealed class FamilyDescriptor
{
    /// <summary>ElementId de la familia (Autodesk.Revit.DB.Family).</summary>
    public ElementId FamilyId { get; set; } = ElementId.InvalidElementId;

    /// <summary>Nombre actual de la familia.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Nombre de la categoría built-in (e.g. "Doors", "Furniture").</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Disciplina derivada de la categoría (Arquitectura, MEP, Estructural, Genérico).</summary>
    public string Discipline { get; set; } = string.Empty;

    /// <summary>Número de tipos (FamilySymbol) que contiene la familia.</summary>
    public int TypeCount { get; set; }

    /// <summary>
    /// Nombre propuesto tras aplicar la regla de renombrado.
    /// Igual a <see cref="Name"/> hasta que el usuario aplique alguna transformación.
    /// </summary>
    public string ProposedName { get; set; } = string.Empty;

    /// <summary>Si <c>true</c> esta familia está seleccionada para la operación actual.</summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Indica si el nombre propuesto difiere del nombre actual.
    /// </summary>
    public bool HasRenameChange =>
        !string.IsNullOrEmpty(ProposedName) && ProposedName != Name;
}
