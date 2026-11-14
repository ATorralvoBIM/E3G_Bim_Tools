using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Services.Sections.Sources;

/// <summary>
/// Origen "Level" del SectionBoxer (Sprint 5).
/// Resuelve los elementos que tienen <c>LevelId</c> igual al solicitado.
/// </summary>
public sealed class LevelSource : ISectionBoxSource
{
    public SectionBoxSourceKind Kind => SectionBoxSourceKind.Level;

    public IReadOnlyList<Element> Resolve(Document doc, SectionBoxRequest request)
    {
        if (request.LevelId is null) return new List<Element>();

        return new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .Where(e =>
            {
                try { return e.LevelId == request.LevelId; }
                catch { return false; }
            })
            .ToList();
    }
}
