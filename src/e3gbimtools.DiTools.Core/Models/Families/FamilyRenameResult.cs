using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado del renombrado masivo de familias (Sprint 7 — FamilyReviser).
/// </summary>
public sealed class FamilyRenameResult
{
    /// <summary>Número de familias renombradas con éxito.</summary>
    public int RenamedCount { get; set; }

    /// <summary>Familias que no pudieron renombrarse y el motivo.</summary>
    public IList<(string OriginalName, string Reason)> Skipped { get; set; }
        = new List<(string, string)>();

    public bool HasErrors => Skipped.Count > 0;

    public string ToSummary()
    {
        if (!HasErrors)
            return $"Se renombraron {RenamedCount} familia(s) correctamente.";

        return $"Renombradas: {RenamedCount}. Omitidas: {Skipped.Count} (ver detalles).";
    }
}
