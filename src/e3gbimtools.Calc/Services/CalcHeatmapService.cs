// ============================================================================
//  CalcHeatmapService.cs
//  Implementacion de IHeatmapService.  Delega 1:1 en Core.Analytics.HeatmapService.
// ============================================================================

using System;
using System.Collections.Generic;
using E3G.BimTools.DiTools.Core.Abstractions;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Core.Analytics;
using e3gbimtools.Calc.Core.Common;

namespace e3gbimtools.Calc.Services
{
    public sealed class CalcHeatmapService : IHeatmapService
    {
        private readonly ILogger _log;
        private readonly HeatmapService _engine = new HeatmapService();

        public CalcHeatmapService(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _log = loggerFactory.CreateLogger<CalcHeatmapService>();
        }

        public HeatmapResult Compute(IList<ResultRow> rows, HeatmapMetric metric)
        {
            try
            {
                _log.Debug("Heatmap: metric={Metric}, filas={Count}.", metric, rows != null ? rows.Count : 0);
                return _engine.Compute(rows, metric);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Heatmap fallo en Compute.");
                throw;
            }
        }
    }
}
