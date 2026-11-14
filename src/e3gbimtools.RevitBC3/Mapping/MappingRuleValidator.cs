using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.BimTools.RevitBC3.Mapping;

// ============================================================
//  MappingRuleValidator  (Sprint A)
// ------------------------------------------------------------
//  Detecta colisiones de partidas BC3 entre reglas habilitadas
//  que viven en el mismo (Capitulo, Subcapitulo).  Es la regla
//  de oro: dentro de un mismo (ChapterCode, SubChapterCode), el
//  BC3Code de las reglas habilitadas debe ser unico.
//
//  Si dos reglas comparten esa terna, el writer las sufija con
//  ".001", ".002"... para cumplir FIEBDC-3, pero TCQ rechaza esas
//  partidas como duplicadas en su modelo nativo.  Este validador
//  hace que el usuario se entere antes de exportar.
//
//  Logica pura del dominio: NO depende de la UI ni de Revit API.
//  Reutilizable en (a) AjustesWindow/RevitBC3MainWindow en tiempo
//  real, (b) la guardia previa al export.
// ============================================================

/// <summary>
/// Representa una colision de partidas duplicadas dentro de un mismo
/// capitulo/subcapitulo.  Contiene la terna identificativa y los Ids de
/// las reglas implicadas.
/// </summary>
public sealed class RuleConflict
{
    /// <summary>Codigo de capitulo (tal como aparece en las reglas).</summary>
    public string ChapterKey { get; init; } = string.Empty;

    /// <summary>Codigo de subcapitulo (puede ser vacio si la regla no usa subcap).</summary>
    public string SubChapterKey { get; init; } = string.Empty;

    /// <summary>Codigo de partida BC3 duplicado.</summary>
    public string BC3Code { get; init; } = string.Empty;

    /// <summary>Ids de las reglas que comparten la terna (siempre 2 o mas).</summary>
    public IReadOnlyList<string> RuleIds { get; init; } = Array.Empty<string>();

    /// <summary>Nombre legible del capitulo (mejor de los disponibles entre las reglas implicadas).</summary>
    public string ChapterName { get; init; } = string.Empty;

    /// <summary>Nombre legible del subcapitulo (idem).</summary>
    public string SubChapterName { get; init; } = string.Empty;

    /// <summary>Etiqueta capitulo "CODIGO Nombre" o solo lo que haya.</summary>
    public string ChapterDisplay
    {
        get
        {
            var c = ChapterKey?.Trim() ?? string.Empty;
            var n = ChapterName?.Trim() ?? string.Empty;
            if (c.Length == 0 && n.Length == 0) return "(sin capitulo)";
            if (c.Length == 0) return n;
            if (n.Length == 0) return c;
            return $"{c} {n}";
        }
    }

    /// <summary>Etiqueta subcapitulo similar a <see cref="ChapterDisplay"/>; vacio si no aplica.</summary>
    public string SubChapterDisplay
    {
        get
        {
            var c = SubChapterKey?.Trim() ?? string.Empty;
            var n = SubChapterName?.Trim() ?? string.Empty;
            if (c.Length == 0 && n.Length == 0) return string.Empty;
            if (c.Length == 0) return n;
            if (n.Length == 0) return c;
            return $"{c} {n}";
        }
    }
}

/// <summary>
/// Validador puro de reglas de mapeo BC3.  Sin estado, sin dependencias UI.
/// </summary>
public static class MappingRuleValidator
{
    // El writer (BC3WriterService) usa StringComparer.Ordinal al contar
    // ocurrencias del codigo BC3.  Para que el validador detecte EXACTAMENTE
    // los mismos casos que el writer sufijaria, usamos tambien Ordinal aqui.
    private static readonly StringComparer Cmp = StringComparer.Ordinal;

    /// <summary>
    /// Devuelve TODOS los conflictos presentes en la coleccion de reglas:
    /// grupos de 2 o mas reglas habilitadas que comparten la misma terna
    /// (ChapterCode, SubChapterCode, BC3Code).  Reglas deshabilitadas o
    /// con BC3Code vacio se ignoran (no pueden colisionar).
    /// </summary>
    public static IReadOnlyList<RuleConflict> DetectConflicts(IEnumerable<MappingRule> rules)
    {
        if (rules is null) return Array.Empty<RuleConflict>();

        var enabled = rules
            .Where(r => r is not null
                        && r.Enabled
                        && !string.IsNullOrWhiteSpace(r.BC3Code))
            .ToList();

        var groups = enabled
            .GroupBy(r => new GroupKey(
                NormalizeKey(r.ChapterCode),
                NormalizeKey(r.SubChapterCode),
                r.BC3Code.Trim()));

        var conflicts = new List<RuleConflict>();
        foreach (var g in groups)
        {
            if (g.Count() < 2) continue;

            var first = g.First();
            conflicts.Add(new RuleConflict
            {
                ChapterKey = first.ChapterCode ?? string.Empty,
                SubChapterKey = first.SubChapterCode ?? string.Empty,
                BC3Code = g.Key.BC3Code,
                ChapterName = g.Select(r => r.ChapterName)
                               .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty,
                SubChapterName = g.Select(r => r.SubChapterName)
                                  .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty,
                RuleIds = g.Select(r => r.Id).ToList(),
            });
        }

        return conflicts;
    }

    /// <summary>
    /// Comprueba si una regla candidata entra en conflicto con alguna otra
    /// regla habilitada del conjunto.  La regla candidata puede o no estar
    /// dentro del conjunto (se reconoce por <c>candidate.Id</c> para no
    /// chocar consigo misma al editar).  Devuelve <c>null</c> si no hay
    /// conflicto o si la candidata esta deshabilitada / sin BC3Code.
    /// </summary>
    public static RuleConflict? FindConflictFor(MappingRule candidate, IEnumerable<MappingRule> allRules)
    {
        if (candidate is null) return null;
        if (!candidate.Enabled) return null;
        if (string.IsNullOrWhiteSpace(candidate.BC3Code)) return null;
        if (allRules is null) return null;

        var key = new GroupKey(
            NormalizeKey(candidate.ChapterCode),
            NormalizeKey(candidate.SubChapterCode),
            candidate.BC3Code.Trim());

        var colliders = allRules
            .Where(r => r is not null
                        && r.Enabled
                        && !ReferenceEquals(r, candidate)
                        && !Cmp.Equals(r.Id, candidate.Id)
                        && !string.IsNullOrWhiteSpace(r.BC3Code)
                        && key.Equals(new GroupKey(
                            NormalizeKey(r.ChapterCode),
                            NormalizeKey(r.SubChapterCode),
                            r.BC3Code.Trim())))
            .ToList();

        if (colliders.Count == 0) return null;

        var ids = new List<string> { candidate.Id };
        ids.AddRange(colliders.Select(r => r.Id));

        var other = colliders[0];
        return new RuleConflict
        {
            ChapterKey = candidate.ChapterCode ?? string.Empty,
            SubChapterKey = candidate.SubChapterCode ?? string.Empty,
            BC3Code = candidate.BC3Code.Trim(),
            ChapterName = !string.IsNullOrWhiteSpace(candidate.ChapterName)
                            ? candidate.ChapterName
                            : other.ChapterName ?? string.Empty,
            SubChapterName = !string.IsNullOrWhiteSpace(candidate.SubChapterName)
                            ? candidate.SubChapterName
                            : other.SubChapterName ?? string.Empty,
            RuleIds = ids,
        };
    }

    /// <summary>
    /// Conjunto de Ids de regla que estan en conflicto, util para marcarlas
    /// visualmente en el grid de un golpe.
    /// </summary>
    public static HashSet<string> GetConflictingRuleIds(IEnumerable<RuleConflict> conflicts)
    {
        var set = new HashSet<string>(Cmp);
        if (conflicts is null) return set;
        foreach (var c in conflicts)
            foreach (var id in c.RuleIds)
                if (!string.IsNullOrEmpty(id)) set.Add(id);
        return set;
    }

    /// <summary>
    /// Construye una etiqueta legible de la regla para mensajes de aviso
    /// (preferimos Name si existe; si no, "Categoria -&gt; BC3Code").
    /// </summary>
    public static string DescribeRule(MappingRule rule)
    {
        if (rule is null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(rule.Name)) return rule.Name.Trim();
        var cat = string.IsNullOrWhiteSpace(rule.CategoryDisplayName)
                    ? "?"
                    : rule.CategoryDisplayName.Trim();
        var code = string.IsNullOrWhiteSpace(rule.BC3Code) ? "?" : rule.BC3Code.Trim();
        return $"{cat} -> {code}";
    }

    // ----- helpers -----

    private static string NormalizeKey(string? value)
        => value?.Trim() ?? string.Empty;

    private readonly record struct GroupKey(string ChapterKey, string SubChapterKey, string BC3Code);
}
