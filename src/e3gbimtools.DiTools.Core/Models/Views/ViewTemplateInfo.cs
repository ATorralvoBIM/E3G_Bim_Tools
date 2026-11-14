using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Views;

/// <summary>
/// Snapshot de una View Template del documento (Sprint 4 - ViewManager).
/// Inmutable: cualquier modificacion pasa por <c>IViewTemplateService</c>.
/// </summary>
public sealed class ViewTemplateInfo
{
    public ViewTemplateInfo(
        ElementId id,
        string name,
        ViewType viewType,
        IReadOnlyList<ElementId> associatedViewIds)
    {
        Id = id;
        Name = name;
        ViewType = viewType;
        AssociatedViewIds = associatedViewIds;
    }

    public ElementId Id { get; }
    public string Name { get; }
    public ViewType ViewType { get; }

    /// <summary>Ids de las vistas que tienen asignada esta plantilla.</summary>
    public IReadOnlyList<ElementId> AssociatedViewIds { get; }

    public int UsageCount => AssociatedViewIds.Count;

    /// <summary>True si ninguna vista del documento la usa.</summary>
    public bool IsOrphan => AssociatedViewIds.Count == 0;
}
