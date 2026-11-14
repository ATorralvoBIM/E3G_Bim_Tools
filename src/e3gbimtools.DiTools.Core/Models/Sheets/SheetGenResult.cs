using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Sheets;

/// <summary>
/// Resultado de una operación de creación masiva de sheets
/// (Sprint 6 — SheetGen).
/// </summary>
public sealed class SheetGenResult
{
    /// <summary>Ids de las sheets creadas correctamente.</summary>
    public IList<ElementId> CreatedSheetIds { get; set; } = new List<ElementId>();

    /// <summary>
    /// Números de sheet que se omitieron (duplicados, title block faltante, etc.)
    /// con el motivo.
    /// </summary>
    public IList<string> SkippedNumbers { get; set; } = new List<string>();

    /// <summary>Advertencias no fatales (ej. vista no encontrada al aplicar layout).</summary>
    public IList<string> Warnings { get; set; } = new List<string>();

    /// <summary>Tiempo total de la operación en milisegundos.</summary>
    public long ElapsedMs { get; set; }

    public bool HasErrors  => SkippedNumbers.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;

    public string ToSummary()
    {
        var ok = CreatedSheetIds.Count;
        var skipped = SkippedNumbers.Count;
        return skipped == 0
            ? $"{ok} sheet(s) creadas en {ElapsedMs} ms."
            : $"{ok} sheet(s) creadas, {skipped} omitidas. Ver advertencias.";
    }
}
