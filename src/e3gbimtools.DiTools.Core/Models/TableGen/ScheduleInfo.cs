using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Informacion resumida de un ViewSchedule del documento, para poblar
/// el ComboBox del tab Schedule → Vista (Sprint 9 — TableGen P2).
/// </summary>
public sealed class ScheduleInfo
{
    public ElementId Id       { get; init; } = ElementId.InvalidElementId;
    public string    Name     { get; init; } = "";
    public string    Category { get; init; } = "";
    public int       RowCount { get; init; }

    /// <summary>Texto para mostrar en el combo: "Nombre (Categoria, N filas)".</summary>
    public string DisplayName => RowCount > 0
        ? $"{Name} ({Category}, {RowCount} fil.)"
        : $"{Name} ({Category})";
}
