using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Filter;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Motor OneFilter (Sprint 2).  Lee categorias y parametros del documento,
/// evalua una <see cref="FilterDefinition"/> y devuelve los elementos que
/// cumplen la expresion logica AND/OR anidada.
///
/// Convencion: este servicio NO abre transacciones (es de lectura).  Las
/// acciones que modifican el modelo (override de color, edicion en lote)
/// las gestionan servicios separados.
/// </summary>
public interface IFilterService
{
    /// <summary>
    /// Categorias visibles del documento ordenadas alfabeticamente.  Filtra las
    /// categorias internas/sin nombre para que la lista sea util en la UI.
    /// </summary>
    IReadOnlyList<CategoryDescriptor> GetCategories(Document doc);

    /// <summary>
    /// Parametros disponibles para una o mas categorias.  Combina parametros de
    /// instancia y de tipo, distinguiendo unos de otros mediante el flag
    /// <see cref="ParameterDescriptor.IsTypeParameter"/>.
    /// </summary>
    IReadOnlyList<ParameterDescriptor> GetParametersForCategories(Document doc, IReadOnlyList<BuiltInCategory> categories);

    /// <summary>
    /// Evalua el filtro contra el documento.  Devuelve los Ids de los elementos
    /// que cumplen, junto con metadatos para la UI (avisos, conteos).
    /// </summary>
    FilterResult Evaluate(Document doc, FilterDefinition definition);

    /// <summary>
    /// Construye filas planas (categoria, familia, tipo, nombre, nivel) a partir
    /// de una lista de Ids.  Se usa para llenar el DataGrid de resultados.
    /// </summary>
    IReadOnlyList<FilterRow> BuildRows(Document doc, IReadOnlyList<ElementId> ids);
}
