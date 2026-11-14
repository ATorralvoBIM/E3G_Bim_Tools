namespace E3G.BimTools.DiTools.Core.Models.Sections;

/// <summary>
/// Tipo de origen del SectionBoxer (Sprint 5).  Determina como resolver la
/// lista de elementos que entrara en el calculo del bounding box.
/// </summary>
public enum SectionBoxSourceKind
{
    /// <summary>Elementos actualmente seleccionados en la UI.</summary>
    Selection,

    /// <summary>Una habitacion/espacio del documento.</summary>
    Room,

    /// <summary>Todos los elementos comprendidos entre dos niveles.</summary>
    Level,

    /// <summary>Resultado de un filtro guardado de OneFilter (Sprint 2).</summary>
    SavedFilter
}
