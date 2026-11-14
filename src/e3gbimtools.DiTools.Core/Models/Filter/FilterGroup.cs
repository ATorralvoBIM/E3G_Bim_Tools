using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Logica para combinar condiciones de un grupo: AND (todas se cumplen) o OR
/// (basta con una).
/// </summary>
public enum FilterLogic
{
    And,
    Or,
}

/// <summary>
/// Grupo logico del editor de filtros.  Permite anidar subgrupos para construir
/// expresiones complejas tipo "(A AND B) OR (C AND D)".
///
/// Convencion de evaluacion (ver <c>FilterService.Evaluate</c>):
///  - El grupo es "match" si su logica AND requiere que TODAS las condiciones
///    y TODOS los subgrupos sean match.  Con OR basta con uno.
///  - Un grupo vacio (sin condiciones ni subgrupos) se considera match (true)
///    para no descartar resultados por error de configuracion.
/// </summary>
public sealed class FilterGroup
{
    public FilterLogic Logic { get; set; } = FilterLogic.And;
    public List<FilterCondition> Conditions { get; set; } = new();
    public List<FilterGroup> SubGroups { get; set; } = new();

    /// <summary>True si el grupo no tiene condiciones ni subgrupos.</summary>
    public bool IsEmpty => Conditions.Count == 0 && SubGroups.Count == 0;
}
