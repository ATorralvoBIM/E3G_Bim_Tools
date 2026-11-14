using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Petición de renombrado masivo de familias (Sprint 7 — FamilyReviser).
/// </summary>
public sealed class FamilyRenameRequest
{
    /// <summary>Familias sobre las que se aplica el renombrado.</summary>
    public IList<FamilyDescriptor> Families { get; set; } = new List<FamilyDescriptor>();

    /// <summary>Prefijo a añadir al inicio del nombre actual.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Sufijo a añadir al final del nombre actual.</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>Cadena a buscar (find/replace).</summary>
    public string FindText { get; set; } = string.Empty;

    /// <summary>Cadena con la que se reemplaza <see cref="FindText"/>.</summary>
    public string ReplaceText { get; set; } = string.Empty;

    /// <summary>Si <c>true</c> el find/replace diferencia mayúsculas.</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// Calcula el nombre propuesto para una familia según las reglas definidas en la petición.
    /// </summary>
    public string ComputeProposedName(string currentName)
    {
        var result = currentName;

        if (!string.IsNullOrEmpty(FindText))
        {
            var comparison = CaseSensitive
                ? System.StringComparison.Ordinal
                : System.StringComparison.OrdinalIgnoreCase;
            result = ReplaceAll(result, FindText, ReplaceText, comparison);
        }

        if (!string.IsNullOrEmpty(Prefix))
            result = Prefix + result;

        if (!string.IsNullOrEmpty(Suffix))
            result = result + Suffix;

        return result;
    }

    // ----------------------------------------------------------------- //

    private static string ReplaceAll(
        string source,
        string find,
        string replace,
        System.StringComparison comparison)
    {
        if (string.IsNullOrEmpty(find)) return source;

        var sb = new System.Text.StringBuilder();
        int start = 0;
        while (true)
        {
            int idx = source.IndexOf(find, start, comparison);
            if (idx < 0)
            {
                sb.Append(source, start, source.Length - start);
                break;
            }
            sb.Append(source, start, idx - start);
            sb.Append(replace);
            start = idx + find.Length;
        }
        return sb.ToString();
    }
}
