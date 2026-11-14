using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Services.Sections.Sources;

/// <summary>
/// Origen "Selection" del SectionBoxer (Sprint 5).
/// Resuelve directamente los <see cref="SectionBoxRequest.SelectedElementIds"/>
/// del request.  El llamante es responsable de poblarlos antes.
/// </summary>
public sealed class SelectionSource : ISectionBoxSource
{
    public SectionBoxSourceKind Kind => SectionBoxSourceKind.Selection;

    public IReadOnlyList<Element> Resolve(Document doc, SectionBoxRequest request)
    {
        if (request.SelectedElementIds is null || request.SelectedElementIds.Count == 0)
            return new List<Element>();

        return request.SelectedElementIds
            .Select(doc.GetElement)
            .Where(e => e is not null)
            .ToList()!;
    }
}
