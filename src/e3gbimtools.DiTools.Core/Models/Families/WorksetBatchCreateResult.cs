using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado de la creación en lote de worksets desde una lista/archivo de texto
/// (FamilyReviser — réplica de "Worksets Management", bloque A).
/// </summary>
public sealed class WorksetBatchCreateResult
{
    /// <summary>Nombres de worksets creados con éxito.</summary>
    public IList<string> Created { get; set; } = new List<string>();

    /// <summary>Nombres descartados porque el workset ya existía en el documento.</summary>
    public IList<string> AlreadyExisting { get; set; } = new List<string>();

    /// <summary>Nombres descartados por estar repetidos dentro de la propia lista de entrada.</summary>
    public IList<string> DuplicateInInput { get; set; } = new List<string>();

    /// <summary>Nombres que no pudieron crearse y el motivo (p. ej. caracteres inválidos).</summary>
    public IList<(string Name, string Reason)> Errors { get; set; }
        = new List<(string, string)>();

    public bool HasErrors => Errors.Count > 0;

    public string ToSummary()
    {
        var parts = new List<string> { $"Creados: {Created.Count}" };
        if (AlreadyExisting.Count > 0)  parts.Add($"Ya existían: {AlreadyExisting.Count}");
        if (DuplicateInInput.Count > 0) parts.Add($"Duplicados en lista: {DuplicateInInput.Count}");
        if (HasErrors)                  parts.Add($"Errores: {Errors.Count}");
        return string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// Planifica la creación: normaliza los nombres (trim, descarta vacíos), detecta
    /// duplicados dentro de la propia lista y nombres que ya existen en el documento
    /// (comparación sin distinguir mayúsculas/minúsculas). Rellena <see cref="AlreadyExisting"/>
    /// y <see cref="DuplicateInInput"/>, y devuelve los nombres únicos pendientes de crear.
    /// </summary>
    public IReadOnlyList<string> Plan(IEnumerable<string> inputNames, IEnumerable<string> existingNames)
    {
        var existing = new HashSet<string>(
            existingNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<string>();

        foreach (var raw in inputNames ?? Enumerable.Empty<string>())
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            if (existing.Contains(name))
            {
                AlreadyExisting.Add(name);
                continue;
            }
            if (!seen.Add(name))
            {
                DuplicateInInput.Add(name);
                continue;
            }
            toCreate.Add(name);
        }

        return toCreate;
    }
}
