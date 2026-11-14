using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Services.Sections.Sources;

/// <summary>
/// Origen "SavedFilter" del SectionBoxer (Sprint 5).
/// Reusa el repositorio de filtros guardados de OneFilter (Sprint 2) via
/// <see cref="IFilterTemplateService"/> y ejecuta la query sobre el doc.
/// </summary>
public sealed class SavedFilterSource : ISectionBoxSource
{
    private readonly IFilterTemplateService _filterTemplates;
    private readonly IFilterService _filterService;

    public SavedFilterSource(IFilterTemplateService filterTemplates, IFilterService filterService)
    {
        _filterTemplates = filterTemplates;
        _filterService = filterService;
    }

    public SectionBoxSourceKind Kind => SectionBoxSourceKind.SavedFilter;

    public IReadOnlyList<Element> Resolve(Document doc, SectionBoxRequest request)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (string.IsNullOrWhiteSpace(request.SavedFilterTemplateId))
            return Array.Empty<Element>();

        // Carga la plantilla de OneFilter (Sprint 2) y la evalúa contra el documento.
        var definition = _filterTemplates.Load(request.SavedFilterTemplateId);
        if (definition is null) return Array.Empty<Element>();

        var result = _filterService.Evaluate(doc, definition);
        return result.MatchedIds
            .Select(doc.GetElement)
            .Where(e => e is not null)
            .ToList()!;
    }
}
