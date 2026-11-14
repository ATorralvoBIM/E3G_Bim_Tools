using System;
using System.Collections.Generic;
using System.Text;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado de crear una vista 3D por cada workset (réplica de la función
/// "Create 3D views by workset" de KEO). Cada vista aísla su workset: ese
/// queda visible y el resto oculto.
/// </summary>
public sealed class WorksetViewCreateResult
{
    /// <summary>Vistas creadas: (nombre del workset, nombre de la vista).</summary>
    public IList<(string WorksetName, string ViewName)> Created { get; } =
        new List<(string, string)>();

    /// <summary>Worksets para los que falló la creación: (nombre, motivo).</summary>
    public IList<(string WorksetName, string Reason)> Errors { get; } =
        new List<(string, string)>();

    public bool HasErrors => Errors.Count > 0;

    public string ToSummary()
    {
        var sb = new StringBuilder();
        sb.Append($"Vistas 3D creadas: {Created.Count}.");
        if (HasErrors) sb.Append($" Errores: {Errors.Count}.");
        return sb.ToString();
    }

    /// <summary>
    /// Devuelve un nombre de vista único frente a <paramref name="existing"/>.
    /// Si el nombre base ya existe, añade un sufijo " (2)", " (3)", … El nombre
    /// devuelto se inserta en <paramref name="existing"/> para que las llamadas
    /// sucesivas no colisionen. Lógica pura (sin Revit) para poder testearse.
    /// </summary>
    public static string MakeUniqueViewName(string baseName, ISet<string> existing)
    {
        if (existing is null) throw new ArgumentNullException(nameof(existing));

        var name = string.IsNullOrWhiteSpace(baseName) ? "Vista" : baseName.Trim();

        if (!existing.Contains(name))
        {
            existing.Add(name);
            return name;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (!existing.Contains(candidate))
            {
                existing.Add(candidate);
                return candidate;
            }
        }
    }
}
