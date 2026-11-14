using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado del renombrado en lote de worksets vía la columna "Nuevo nombre"
/// (FamilyReviser — réplica de "Worksets Management", bloque B).
/// </summary>
public sealed class WorksetRenameResult
{
    /// <summary>Número de worksets renombrados con éxito.</summary>
    public int RenamedCount { get; set; }

    /// <summary>Renombrados omitidos y el motivo (nombre vacío, colisión, duplicado…).</summary>
    public IList<(string OriginalName, string ProposedName, string Reason)> Skipped { get; set; }
        = new List<(string, string, string)>();

    public bool HasErrors => Skipped.Count > 0;

    public string ToSummary()
    {
        if (!HasErrors)
            return $"Se renombraron {RenamedCount} workset(s) correctamente.";
        return $"Renombrados: {RenamedCount}. Omitidos: {Skipped.Count} (ver detalles).";
    }

    /// <summary>
    /// Valida los cambios de nombre propuestos contra el conjunto de nombres actuales.
    /// Descarta nombres propuestos vacíos, los que colisionan con un workset que se queda
    /// (no se renombra) y los duplicados dentro del propio lote (sin distinguir
    /// mayúsculas/minúsculas). Rellena <see cref="Skipped"/> y devuelve los cambios válidos
    /// a aplicar como pares (Original, Propuesto).
    /// </summary>
    public IReadOnlyList<(string Original, string Proposed)> Plan(
        IEnumerable<(string Original, string Proposed)> changes,
        IEnumerable<string> allCurrentNames)
    {
        var changeList = (changes ?? Enumerable.Empty<(string, string)>()).ToList();

        var beingRenamedFrom = new HashSet<string>(
            changeList.Select(c => c.Original), StringComparer.OrdinalIgnoreCase);

        // Nombres que permanecen tras los renombrados (los que no se tocan).
        var staying = new HashSet<string>(
            (allCurrentNames ?? Enumerable.Empty<string>())
                .Where(n => !beingRenamedFrom.Contains(n)),
            StringComparer.OrdinalIgnoreCase);

        var proposedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valid        = new List<(string, string)>();

        foreach (var (orig, proposedRaw) in changeList)
        {
            var proposed = proposedRaw?.Trim();

            if (string.IsNullOrEmpty(proposed))
            {
                Skipped.Add((orig, proposedRaw ?? string.Empty, "Nombre propuesto vacío."));
                continue;
            }
            if (string.Equals(proposed, orig, StringComparison.OrdinalIgnoreCase))
                continue; // sin cambio real

            if (staying.Contains(proposed))
            {
                Skipped.Add((orig, proposed, "Ya existe un workset con ese nombre."));
                continue;
            }
            if (!proposedSeen.Add(proposed))
            {
                Skipped.Add((orig, proposed, "Nombre propuesto duplicado en el lote."));
                continue;
            }

            valid.Add((orig, proposed));
        }

        return valid;
    }
}
