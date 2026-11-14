using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado del export de familias a disco (Sprint 7 — FamilyReviser).
/// </summary>
public sealed class FamilyExportResult
{
    /// <summary>Número de familias exportadas con éxito.</summary>
    public int ExportedCount { get; set; }

    /// <summary>Familias que no pudieron exportarse y el motivo.</summary>
    public IList<(string FamilyName, string Reason)> Skipped { get; set; }
        = new List<(string, string)>();

    public bool HasErrors => Skipped.Count > 0;

    public string ToSummary()
    {
        if (!HasErrors)
            return $"Se exportaron {ExportedCount} familia(s) correctamente.";

        return $"Exportadas: {ExportedCount}. Omitidas: {Skipped.Count} (ver detalles).";
    }
}
