using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Definicion completa de un filtro OneFilter: categorias a las que aplicar y
/// arbol logico de grupos/condiciones.  Se serializa a JSON al guardar como
/// plantilla (ver <c>IFilterTemplateService</c>).
/// </summary>
public sealed class FilterDefinition
{
    /// <summary>Nombre legible para la plantilla.  Vacio = filtro sin nombre.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Categorias activas (BuiltInCategory representada como int para serializar limpio).</summary>
    public List<int> CategoryIds { get; set; } = new();

    /// <summary>Raiz del arbol logico.  Si esta vacio, el filtro devuelve todos los elementos de las categorias.</summary>
    public FilterGroup Root { get; set; } = new();

    /// <summary>
    /// Convierte los Ids guardados al enum BuiltInCategory para uso en runtime.
    /// </summary>
    public IEnumerable<BuiltInCategory> GetCategories()
    {
        foreach (var id in CategoryIds)
            yield return (BuiltInCategory)id;
    }
}
