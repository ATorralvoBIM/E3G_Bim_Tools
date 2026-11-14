// ============================================================================
//  ICalcFilterService.cs
//  Envoltura fina sobre Filtering.FilterService.  No se llama IFilterService
//  para no chocar con el IFilterService de OneFilter en DiTools.Core.
// ============================================================================

using System;
using System.Collections.Generic;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Filtering;

namespace e3gbimtools.Calc.Abstractions
{
    public interface ICalcFilterService
    {
        /// <summary>Filas del camino critico (opcional, para el flag OnlyCriticalPath).</summary>
        ISet<ResultRow> CriticalPathRows { get; set; }

        /// <summary>Evalua todas las filas y devuelve las que cumplen el criterio.</summary>
        HashSet<ResultRow> Evaluate(IList<ResultRow> rows, FilterCriteria criteria);

        /// <summary>Predicado para CollectionView.Filter (una llamada por fila).</summary>
        Predicate<object> ToCollectionViewPredicate(FilterCriteria criteria);
    }
}
