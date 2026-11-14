// ============================================================================
//  CalcFilterServiceImpl.cs
//  Implementacion de ICalcFilterService.  Delega 1:1 en Filtering.FilterService.
//  La clase se llama "Impl" para no chocar con el FilterService existente
//  (la clase migrada del monolito).
// ============================================================================

using System;
using System.Collections.Generic;
using E3G.BimTools.DiTools.Core.Abstractions;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Filtering;

namespace e3gbimtools.Calc.Services
{
    public sealed class CalcFilterServiceImpl : ICalcFilterService
    {
        private readonly ILogger _log;
        private readonly FilterService _engine = new FilterService();

        public CalcFilterServiceImpl(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _log = loggerFactory.CreateLogger<CalcFilterServiceImpl>();
        }

        public ISet<ResultRow> CriticalPathRows
        {
            get => _engine.CriticalPathRows;
            set => _engine.CriticalPathRows = value;
        }

        public HashSet<ResultRow> Evaluate(IList<ResultRow> rows, FilterCriteria criteria)
        {
            try
            {
                return _engine.Evaluate(rows, criteria);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "FilterService.Evaluate fallo.");
                throw;
            }
        }

        public Predicate<object> ToCollectionViewPredicate(FilterCriteria criteria)
            => _engine.ToCollectionViewPredicate(criteria);
    }
}
