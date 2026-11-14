using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado de la auditoría de worksets (Sprint 7 — FamilyReviser).
/// </summary>
public sealed class WorksetAuditResult
{
    /// <summary>
    /// Elementos que están en un workset diferente al esperado según la regla
    /// de asignación activa. Contiene (ElementId.IntegerValue, WorksetActual, WorksetEsperado).
    /// </summary>
    public IList<(int ElementIdValue, string ActualWorkset, string ExpectedWorkset)> Inconsistencies { get; set; }
        = new List<(int, string, string)>();

    /// <summary>Worksets sin ningún elemento.</summary>
    public IList<string> EmptyWorksets { get; set; } = new List<string>();

    public bool IsClean => Inconsistencies.Count == 0;

    public string ToSummary()
    {
        if (IsClean && EmptyWorksets.Count == 0)
            return "Worksets limpios. Sin inconsistencias ni worksets vacíos.";

        var parts = new List<string>();
        if (Inconsistencies.Count > 0)
            parts.Add($"{Inconsistencies.Count} inconsistencia(s)");
        if (EmptyWorksets.Count > 0)
            parts.Add($"{EmptyWorksets.Count} workset(s) vacío(s)");
        return string.Join(", ", parts) + ".";
    }
}
