using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado de aplicar la visibilidad de worksets sobre un conjunto de vistas
/// (FamilyReviser — réplica de "Worksets Management", bloque E).
/// </summary>
public sealed class WorksetVisibilityResult
{
    /// <summary>Número de vistas en las que se aplicó (que soportan visibilidad de workset).</summary>
    public int ViewsAffected { get; set; }

    /// <summary>Número de pares (vista, workset) cuyo estado de visibilidad cambió.</summary>
    public int ChangedCount { get; set; }

    /// <summary>Pares (vista, workset) que ya estaban en el estado deseado.</summary>
    public int AlreadyCount { get; set; }

    /// <summary>Vistas omitidas por no soportar visibilidad de workset (cronogramas, leyendas…).</summary>
    public int SkippedViews { get; set; }

    /// <summary>Errores por vista y su motivo.</summary>
    public IList<(string ViewName, string Reason)> Errors { get; set; }
        = new List<(string, string)>();

    public bool HasErrors => Errors.Count > 0;

    public string ToSummary()
    {
        var parts = new List<string>
        {
            $"Vistas afectadas: {ViewsAffected}",
            $"Cambios: {ChangedCount}",
        };
        if (AlreadyCount > 0) parts.Add($"Ya estaban: {AlreadyCount}");
        if (SkippedViews > 0) parts.Add($"Vistas no soportadas: {SkippedViews}");
        if (HasErrors)        parts.Add($"Errores: {Errors.Count}");
        return string.Join(", ", parts) + ".";
    }
}
