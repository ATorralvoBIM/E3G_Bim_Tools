// ============================================================================
//  IHeatmapService.cs
//  Envoltura fina sobre Core.Analytics.HeatmapService.
// ============================================================================

using System.Collections.Generic;
using e3gbimtools.Calc.Core.Analytics;
using e3gbimtools.Calc.Core.Common;

namespace e3gbimtools.Calc.Abstractions
{
    public interface IHeatmapService
    {
        /// <summary>Calcula valores y pinceladas (Brush) por fila para un metric dado.</summary>
        HeatmapResult Compute(IList<ResultRow> rows, HeatmapMetric metric);
    }
}
