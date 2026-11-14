namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Regla de organización de carpetas para el export de familias a disco
/// (Sprint 7 — FamilyReviser).
///
/// La plantilla se especifica con tokens encerrados entre llaves, por ejemplo:
///   "{Categoria}/{Disciplina}/{Familia}.rfa"
///
/// Tokens soportados:
///   {Categoria}   → CategoryName de <see cref="FamilyDescriptor"/>
///   {Disciplina}  → Discipline de <see cref="FamilyDescriptor"/>
///   {Familia}     → Name (o ProposedName si se ha renombrado) de la familia
/// </summary>
public sealed class FamilyExportRule
{
    /// <summary>Plantilla de ruta relativa. Default: "{Categoria}\{Familia}.rfa".</summary>
    public string PathTemplate { get; set; } = @"{Categoria}\{Familia}.rfa";

    /// <summary>
    /// Resuelve la ruta relativa de una familia según la plantilla.
    /// Los caracteres inválidos para el sistema de archivos se reemplazan por "_".
    /// </summary>
    public string Resolve(FamilyDescriptor family)
    {
        var name = string.IsNullOrEmpty(family.ProposedName) ? family.Name : family.ProposedName;

        var path = PathTemplate
            .Replace("{Categoria}",  Sanitize(family.CategoryName))
            .Replace("{Disciplina}", Sanitize(family.Discipline))
            .Replace("{Familia}",    Sanitize(name));

        // Garantizar extensión .rfa
        if (!path.EndsWith(".rfa", System.StringComparison.OrdinalIgnoreCase))
            path += ".rfa";

        return path;
    }

    // ----------------------------------------------------------------- //

    private static string Sanitize(string segment)
    {
        if (string.IsNullOrEmpty(segment)) return "_";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(segment.Length);
        foreach (var c in segment)
            sb.Append(invalid.Contains(c) ? '_' : c);

        return sb.ToString();
    }
}
