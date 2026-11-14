using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Sheets;

/// <summary>
/// Resultado de la auditoría del juego de planos (Sprint 6 — SheetGen).
/// </summary>
public sealed class SheetAuditResult
{
    /// <summary>Sheets sin ningún viewport colocado.</summary>
    public IList<SheetInfo> OrphanSheets { get; set; } = new List<SheetInfo>();

    /// <summary>
    /// Sheet Numbers que aparecen más de una vez en el documento.
    /// Contiene el número duplicado (puede abrirse una investigación manualmente).
    /// </summary>
    public IList<string> DuplicateNumbers { get; set; } = new List<string>();

    public bool IsClean => OrphanSheets.Count == 0 && DuplicateNumbers.Count == 0;

    public string ToSummary()
    {
        if (IsClean) return "Juego de planos limpio. Sin huérfanas ni duplicados.";
        var parts = new List<string>();
        if (OrphanSheets.Count > 0)    parts.Add($"{OrphanSheets.Count} sheet(s) huérfana(s)");
        if (DuplicateNumbers.Count > 0) parts.Add($"{DuplicateNumbers.Count} número(s) duplicado(s)");
        return string.Join(", ", parts) + ".";
    }
}
