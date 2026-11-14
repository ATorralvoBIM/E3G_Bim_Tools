using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Views;

namespace E3G.BimTools.DiTools.Core.Services.Views;

/// <summary>
/// Compara dos View Templates campo por campo (Sprint 4).
/// Solo lectura: nunca abre transaccion.
/// </summary>
public sealed class ViewTemplateComparer : IViewTemplateComparer
{
    private readonly ILogger _logger;

    public ViewTemplateComparer(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<ViewTemplateComparer>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public ViewTemplateDiff Compare(Document doc, ElementId templateAId, ElementId templateBId)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var a = doc.GetElement(templateAId) as View;
        var b = doc.GetElement(templateBId) as View;
        if (a is null || !a.IsTemplate) throw new ArgumentException("Template A invalido.", nameof(templateAId));
        if (b is null || !b.IsTemplate) throw new ArgumentException("Template B invalido.", nameof(templateBId));

        var diffs = new List<ViewTemplateFieldDiff>();

        // ---- General ----
        AddIfDifferent(diffs, "General", "Escala", a.Scale.ToString(), b.Scale.ToString());
        AddIfDifferent(diffs, "General", "Detail level", a.DetailLevel.ToString(), b.DetailLevel.ToString());
        AddIfDifferent(diffs, "General", "Discipline", a.Discipline.ToString(), b.Discipline.ToString());
        AddIfDifferent(diffs, "General", "Display style", a.DisplayStyle.ToString(), b.DisplayStyle.ToString());

        // ---- V/G Overrides por categoria (visibilidad + overrides graficos) ----
        foreach (Category cat in doc.Settings.Categories)
        {
            if (cat is null) continue;
            if (cat.CategoryType is not (CategoryType.Model or CategoryType.Annotation)) continue;

            ElementId catId = cat.Id;

            // Visibilidad.
            try
            {
                bool visA = !a.GetCategoryHidden(catId);
                bool visB = !b.GetCategoryHidden(catId);
                AddIfDifferent(diffs, "V/G", $"{cat.Name}: visible", visA ? "sí" : "no", visB ? "sí" : "no");
            }
            catch { /* la categoria no admite control de visibilidad: se ignora */ }

            // Overrides graficos.
            try
            {
                string sa = OverrideSig(a.GetCategoryOverrides(catId));
                string sb = OverrideSig(b.GetCategoryOverrides(catId));
                AddIfDifferent(diffs, "V/G", $"{cat.Name}: overrides", sa, sb);
            }
            catch { /* sin overrides para esta categoria */ }
        }

        // ---- Filtros aplicados ----
        try
        {
            var fa = a.GetFilters();
            var fb = b.GetFilters();
            var todos = fa.Concat(fb).Distinct();
            foreach (var fId in todos)
            {
                string nombre = doc.GetElement(fId)?.Name ?? $"Filtro {fId}";
                bool inA = fa.Contains(fId), inB = fb.Contains(fId);
                AddIfDifferent(diffs, "Filtros", $"{nombre}: aplicado", inA ? "sí" : "no", inB ? "sí" : "no");

                // Si está en ambas, comparamos visibilidad y overrides del filtro.
                if (inA && inB)
                {
                    try
                    {
                        AddIfDifferent(diffs, "Filtros", $"{nombre}: visible",
                            a.GetFilterVisibility(fId) ? "sí" : "no",
                            b.GetFilterVisibility(fId) ? "sí" : "no");
                        AddIfDifferent(diffs, "Filtros", $"{nombre}: overrides",
                            OverrideSig(a.GetFilterOverrides(fId)),
                            OverrideSig(b.GetFilterOverrides(fId)));
                    }
                    catch { /* ignore */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("ViewTemplateComparer: no se pudieron comparar filtros: {0}", ex.Message);
        }

        return new ViewTemplateDiff(templateAId, a.Name, templateBId, b.Name, diffs);
    }

    private static void AddIfDifferent(List<ViewTemplateFieldDiff> diffs, string section, string fieldName, string? valueA, string? valueB)
    {
        if (string.Equals(valueA, valueB, StringComparison.Ordinal)) return;
        diffs.Add(new ViewTemplateFieldDiff(section, fieldName, valueA, valueB));
    }

    /// <summary>Firma compacta de un OverrideGraphicSettings para comparar sin volcar todo.</summary>
    private static string OverrideSig(OverrideGraphicSettings o)
    {
        if (o is null) return "-";
        static string C(Color c) => c is not null && c.IsValid ? $"{c.Red},{c.Green},{c.Blue}" : "-";
        return $"pl={C(o.ProjectionLineColor)};plw={o.ProjectionLineWeight};" +
               $"cl={C(o.CutLineColor)};clw={o.CutLineWeight};" +
               $"sf={C(o.SurfaceForegroundPatternColor)};cf={C(o.CutForegroundPatternColor)};" +
               $"ht={o.Halftone};tr={o.Transparency}";
    }
}
