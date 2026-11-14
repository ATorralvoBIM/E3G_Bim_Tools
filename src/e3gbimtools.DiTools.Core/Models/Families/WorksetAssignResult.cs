using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado de la asignación masiva de elementos a un workset (Sprint 7 — FamilyReviser).
/// </summary>
public sealed class WorksetAssignResult
{
    /// <summary>Número de elementos reasignados con éxito.</summary>
    public int AssignedCount { get; set; }

    /// <summary>Número de elementos que ya estaban en el workset destino (omitidos).</summary>
    public int AlreadyInTargetCount { get; set; }

    /// <summary>Elementos que no pudieron reasignarse y el motivo.</summary>
    public IList<(int ElementIdValue, string Reason)> Errors { get; set; }
        = new List<(int, string)>();

    public bool HasErrors => Errors.Count > 0;

    public string ToSummary()
    {
        var parts = new List<string>();
        parts.Add($"Reasignados: {AssignedCount}");
        if (AlreadyInTargetCount > 0)
            parts.Add($"Ya estaban: {AlreadyInTargetCount}");
        if (HasErrors)
            parts.Add($"Errores: {Errors.Count}");
        return string.Join(", ", parts) + ".";
    }
}
