namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Ámbito de vistas sobre el que aplicar la visibilidad de worksets
/// (FamilyReviser — réplica de "Worksets Management", bloque E).
/// </summary>
public enum WorksetViewScope
{
    /// <summary>Solo la vista activa del documento.</summary>
    ActiveView,

    /// <summary>Todas las vistas que no son plantilla.</summary>
    AllViews,

    /// <summary>Todas las plantillas de vista.</summary>
    AllViewTemplates,

    /// <summary>Todas las vistas y todas las plantillas de vista.</summary>
    AllViewsAndTemplates,
}
