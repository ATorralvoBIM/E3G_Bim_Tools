using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Inspector de parametros de una categoria Revit.
/// Toma un elemento representativo y lee sus parametros de instancia y de tipo.
/// </summary>
public interface IRevitParameterInspector
{
    /// <summary>
    /// Lee los parametros de la categoria indicada usando el primer elemento
    /// representativo encontrado en el documento.  Devuelve listas vacias
    /// si la categoria no tiene elementos.
    /// </summary>
    CategoryParameters GetParameters(Document doc, BuiltInCategory category);

    /// <summary>
    /// Devuelve los valores DISTINTOS que el parametro indicado toma en los
    /// elementos de la categoria.  Si el parametro no esta en la instancia,
    /// se busca tambien en el tipo del elemento.  Ordenado alfabeticamente.
    /// </summary>
    IReadOnlyList<string> GetParameterValues(Document doc, BuiltInCategory category, string parameterName);
}
