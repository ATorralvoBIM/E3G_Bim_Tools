using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

public sealed class FilterImportOptions
{
    /// <summary>Añade a la vista los filtros del JSON que existen en el proyecto pero no están aplicados.</summary>
    public bool AddMissingToView      { get; set; } = true;
    /// <summary>Crea en el proyecto los filtros del JSON que no existen (requiere definición en el JSON).</summary>
    public bool CreateMissingFilters  { get; set; } = true;
    /// <summary>Si existe un filtro homónimo con reglas distintas, sustituye sus reglas por las del JSON.</summary>
    public bool UpdateDifferentRules  { get; set; }
    /// <summary>Pone los filtros del JSON primero (más prioridad), en el orden del JSON.</summary>
    public bool KeepJsonOrder         { get; set; }
    /// <summary>Quita de la vista los filtros que no están en el JSON.</summary>
    public bool RemoveNotInJson       { get; set; }
}

/// <summary>Una línea de la vista previa / informe de importación.</summary>
public sealed class FilterImportPlanRow
{
    public string Target  { get; init; } = string.Empty;
    public string Filter  { get; init; } = string.Empty;
    public string Action  { get; init; } = string.Empty;
    /// <summary>0 = sin cambios, 1 = cambio, 2 = aviso/omitido.</summary>
    public int    Level   { get; init; }
}

/// <summary>
/// Motor de importación. El mismo recorrido sirve para la vista previa
/// (<c>dryRun = true</c>, sin escribir) y para aplicar, así lo que se ve en la
/// vista previa es exactamente lo que se hace. Aplicar requiere transacción abierta.
/// </summary>
internal sealed class FilterGraphicsImporter
{
    private static readonly JsonSerializerOptions CompareOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Document _doc;
    private readonly FilterGraphicsPreset _preset;
    private readonly FilterImportOptions _opts;

    public FilterGraphicsImporter(Document doc, FilterGraphicsPreset preset, FilterImportOptions opts)
    {
        _doc = doc;
        _preset = preset;
        _opts = opts;
    }

    public HashSet<string> MissingPatterns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<FilterImportPlanRow> Run(IReadOnlyList<View> targets, bool dryRun)
    {
        var rows = new List<FilterImportPlanRow>();
        var mapper = new FilterGraphicsMapper(_doc);

        // 1) Nivel proyecto: qué filtros existen, cuáles se crean y cuáles difieren.
        var byName = new FilteredElementCollector(_doc)
            .OfClass(typeof(FilterElement))
            .Cast<FilterElement>()
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var resolved = new List<(FilterGraphicsEntry Entry, ElementId Id)>();
        foreach (var e in _preset.Filters)
        {
            if (byName.TryGetValue(e.Name, out var existing))
            {
                if (e.Definition is not null && existing is ParameterFilterElement pfe &&
                    !FilterDefinitionSerializer.AreEquivalent(FilterDefinitionSerializer.Read(_doc, pfe), e.Definition))
                {
                    if (_opts.UpdateDifferentRules)
                    {
                        string? err = null;
                        bool ok = dryRun || FilterDefinitionSerializer.Update(_doc, pfe, e.Definition, out err);
                        rows.Add(Project(e.Name, ok ? "Reglas distintas: se actualizan con las del JSON" : $"No se pudieron actualizar las reglas: {err}", ok ? 1 : 2));
                    }
                    else
                    {
                        rows.Add(Project(e.Name, "Reglas distintas en este proyecto: se mantienen (solo se aplica el grafismo)", 2));
                    }
                }
                resolved.Add((e, existing.Id));
                continue;
            }

            if (!_opts.CreateMissingFilters || e.Definition is null)
            {
                rows.Add(Project(e.Name, e.Definition is null
                    ? "No existe en el proyecto (el JSON no trae sus reglas)"
                    : "No existe en el proyecto (crear filtros desactivado)", 2));
                continue;
            }

            if (dryRun)
            {
                rows.Add(Project(e.Name, "Se crea el filtro en el proyecto", 1));
                resolved.Add((e, ElementId.InvalidElementId));   // marcador: "se creará"
                continue;
            }

            var created = FilterDefinitionSerializer.Create(_doc, e.Name, e.Definition, out var error);
            if (created is null)
            {
                rows.Add(Project(e.Name, $"No se pudo crear: {error}", 2));
                continue;
            }
            rows.Add(Project(e.Name, "Filtro creado en el proyecto", 1));
            resolved.Add((e, created.Id));
        }

        // 2) Nivel vista/plantilla.
        foreach (var view in targets)
        {
            var label = (view.IsTemplate ? "Plantilla: " : "Vista: ") + view.Name;
            var onView = new HashSet<ElementId>(OrderedFilters(view));

            foreach (var (e, id) in resolved)
            {
                try { ApplyToView(view, label, e, id, onView, mapper, rows, dryRun); }
                catch (Exception ex)
                {
                    rows.Add(Row(label, e.Name, $"No se pudo aplicar en esta vista: {ex.Message}", 2));
                }
            }
            var jsonIds = new HashSet<ElementId>(resolved.Select(r => r.Id).Where(i => i != ElementId.InvalidElementId));

            if (_opts.RemoveNotInJson)
            {
                foreach (var id in OrderedFilters(view).Where(i => !jsonIds.Contains(i)).ToList())
                {
                    var name = _doc.GetElement(id)?.Name ?? id.Value.ToString();
                    if (!dryRun) view.RemoveFilter(id);
                    rows.Add(Row(label, name, "Se quita de la vista (no está en el JSON)", 1));
                }
            }

            if (_opts.KeepJsonOrder)
            {
                var desired = resolved.Select(r => r.Id).Where(i => i != ElementId.InvalidElementId && (dryRun ? true : onView.Contains(i))).ToList();
                bool changed = dryRun ? true : Reorder(view, desired);
                if (changed) rows.Add(Row(label, "(orden)", dryRun
                    ? "Se reordenan si hace falta: filtros del JSON primero, en su orden"
                    : "Reordenados: filtros del JSON primero, en su orden", 1));
            }
        }

        foreach (var p in mapper.MissingPatterns) MissingPatterns.Add(p);
        return rows;
    }

    public static string Summarize(IReadOnlyList<FilterImportPlanRow> rows, IReadOnlyCollection<string> missingPatterns)
    {
        var sb = new StringBuilder();
        int changes = rows.Count(r => r.Level == 1);
        int same    = rows.Count(r => r.Level == 0);
        var warns   = rows.Where(r => r.Level == 2).ToList();

        sb.AppendLine($"Cambios aplicados: {changes}   ·   Sin cambios: {same}   ·   Avisos: {warns.Count}");
        if (warns.Count > 0)
        {
            sb.AppendLine().AppendLine("Avisos:");
            foreach (var w in warns.Take(25)) sb.AppendLine($"  • {w.Filter}: {w.Action}");
            if (warns.Count > 25) sb.AppendLine($"  … y {warns.Count - 25} más");
        }
        if (missingPatterns.Count > 0)
        {
            sb.AppendLine().AppendLine("Patrones no encontrados (se dejaron sin modificar):");
            foreach (var p in missingPatterns.Take(25)) sb.AppendLine($"  • {p}");
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ //

    private void ApplyToView(View view, string label, FilterGraphicsEntry e, ElementId id,
        HashSet<ElementId> onView, FilterGraphicsMapper mapper, List<FilterImportPlanRow> rows, bool dryRun)
    {
        bool willCreate = id == ElementId.InvalidElementId;
        if (willCreate || !onView.Contains(id))
        {
            if (!_opts.AddMissingToView)
            {
                rows.Add(Row(label, e.Name, "No está en la vista: se omite (añadir desactivado)", 2));
                return;
            }
            if (!dryRun)
            {
                view.AddFilter(id);
                onView.Add(id);
                ApplyEntry(view, id, e, mapper);
            }
            rows.Add(Row(label, e.Name, "Se añade a la vista con su grafismo", 1));
            return;
        }

        bool same = Same(mapper.Read(view, id, e.Name), e);
        if (!dryRun && !same) ApplyEntry(view, id, e, mapper);
        rows.Add(Row(label, e.Name, same ? "Sin cambios" : "Cambia el grafismo", same ? 0 : 1));
    }

    private static void ApplyEntry(View view, ElementId id, FilterGraphicsEntry e, FilterGraphicsMapper mapper)
    {
        view.SetFilterOverrides(id, mapper.Build(e));
        view.SetFilterVisibility(id, e.Visible);
        view.SetIsFilterEnabled(id, e.Enabled);
    }

    private static bool Same(FilterGraphicsEntry current, FilterGraphicsEntry wanted)
    {
        var a = current.GraphicsOnly(); a.Name = string.Empty;
        var b = wanted.GraphicsOnly();  b.Name = string.Empty;
        return JsonSerializer.Serialize(a, CompareOpts) == JsonSerializer.Serialize(b, CompareOpts);
    }

    /// <summary>
    /// Reordena: <paramref name="first"/> arriba en ese orden y el resto detrás
    /// en su orden actual. La API no tiene "mover filtro", así que se quitan y
    /// se vuelven a añadir conservando su grafismo, visibilidad y activación.
    /// </summary>
    private static bool Reorder(View view, IReadOnlyList<ElementId> first)
    {
        var current = OrderedFilters(view).ToList();
        var desired = first.Where(current.Contains).Concat(current.Where(i => !first.Contains(i))).ToList();
        if (desired.SequenceEqual(current)) return false;

        var state = current.ToDictionary(id => id, id => (
            Ogs: view.GetFilterOverrides(id),
            Visible: view.GetFilterVisibility(id),
            Enabled: view.GetIsFilterEnabled(id)));

        foreach (var id in current) view.RemoveFilter(id);
        foreach (var id in desired)
        {
            view.AddFilter(id);
            var s = state[id];
            view.SetFilterOverrides(id, s.Ogs);
            view.SetFilterVisibility(id, s.Visible);
            view.SetIsFilterEnabled(id, s.Enabled);
        }
        return true;
    }

    public static IEnumerable<ElementId> OrderedFilters(View view)
    {
        try { return view.GetOrderedFilters(); }
        catch { return view.GetFilters(); }
    }

    private static FilterImportPlanRow Project(string filter, string action, int level)
        => new() { Target = "(proyecto)", Filter = filter, Action = action, Level = level };

    private static FilterImportPlanRow Row(string target, string filter, string action, int level)
        => new() { Target = target, Filter = filter, Action = action, Level = level };
}
