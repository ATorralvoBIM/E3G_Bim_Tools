using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Sheets;

/// <summary>
/// Resumen de una Drawing Sheet existente en el documento
/// (Sprint 6 — SheetGen).  Usado en auditoría y export a Excel.
/// </summary>
public sealed class SheetInfo
{
    public ElementId ElementId { get; set; } = ElementId.InvalidElementId;
    public string SheetNumber { get; set; } = string.Empty;
    public string SheetName   { get; set; } = string.Empty;

    /// <summary>Número de viewports colocados en la sheet (0 = huérfana).</summary>
    public int ViewCount { get; set; }

    /// <summary>Disciplina (parámetro de proyecto, puede ser vacío).</summary>
    public string Discipline { get; set; } = string.Empty;
}
