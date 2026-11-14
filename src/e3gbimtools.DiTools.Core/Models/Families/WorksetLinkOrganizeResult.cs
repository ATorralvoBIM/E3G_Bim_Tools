using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Resultado de organizar vínculos (Revit/CAD) en worksets dedicados, creando un workset
/// por cada vínculo y moviendo sus instancias a él
/// (FamilyReviser — réplica de "Worksets Management", bloque D).
/// </summary>
public sealed class WorksetLinkOrganizeResult
{
    /// <summary>Nombres de worksets creados para alojar vínculos.</summary>
    public IList<string> WorksetsCreated { get; set; } = new List<string>();

    /// <summary>Nombres de worksets ya existentes que se reutilizaron.</summary>
    public IList<string> WorksetsReused { get; set; } = new List<string>();

    /// <summary>Número de instancias de vínculo reasignadas a su workset.</summary>
    public int InstancesAssigned { get; set; }

    /// <summary>Errores (creación de workset o asignación) y su motivo.</summary>
    public IList<(string Name, string Reason)> Errors { get; set; }
        = new List<(string, string)>();

    public bool HasErrors => Errors.Count > 0;

    public string ToSummary()
    {
        var parts = new List<string>
        {
            $"Worksets creados: {WorksetsCreated.Count}",
            $"Instancias asignadas: {InstancesAssigned}",
        };
        if (WorksetsReused.Count > 0) parts.Add($"Reutilizados: {WorksetsReused.Count}");
        if (HasErrors)               parts.Add($"Errores: {Errors.Count}");
        return string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// Construye un nombre de workset válido a partir del nombre de un tipo de vínculo:
    /// quita el sufijo de posición " : N" de las instancias, reemplaza los caracteres no
    /// admitidos en nombres de workset por '_', antepone el prefijo y recorta a
    /// <paramref name="maxLength"/>. Nunca devuelve cadena vacía.
    /// </summary>
    public static string SanitizeWorksetName(string prefix, string rawName, int maxLength = 100)
    {
        var name = (rawName ?? string.Empty).Trim();

        // Las instancias de vínculo se llaman "Tipo : N" → quedarnos con "Tipo".
        var posIdx = name.LastIndexOf(" : ", System.StringComparison.Ordinal);
        if (posIdx > 0) name = name.Substring(0, posIdx).Trim();

        var invalid = new HashSet<char>(new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' });
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);

        var cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0) cleaned = "Link";

        var full = ((prefix ?? string.Empty).Trim() + cleaned).Trim();
        if (full.Length > maxLength)
            full = full.Substring(0, maxLength).Trim();

        return full;
    }
}
