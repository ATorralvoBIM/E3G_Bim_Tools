using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Resultado de una evaluacion de OneFilter: elementos que cumplen el filtro
/// + avisos generados durante la evaluacion (parametros no encontrados,
/// conversiones imposibles, etc.).
/// </summary>
public sealed class FilterResult
{
    public List<ElementId> MatchedIds { get; } = new();
    public List<string> Warnings { get; } = new();
    public int ElementsEvaluated { get; set; }
    public int CategoriesEvaluated { get; set; }

    public string ToSummary()
    {
        return $"{MatchedIds.Count} elemento(s) en {CategoriesEvaluated} categoria(s); " +
               $"{ElementsEvaluated} evaluado(s); {Warnings.Count} aviso(s).";
    }
}
