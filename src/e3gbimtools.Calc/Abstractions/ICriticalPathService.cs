// ============================================================================
//  ICriticalPathService.cs
//  Envoltura fina sobre Core.Analytics.CriticalPathService. Permite que la
//  ventana CalcWindow resuelva el servicio via DI y obtenga el camino critico.
// ============================================================================

using System.Collections.Generic;
using e3gbimtools.Calc.Core.Analytics;
using e3gbimtools.Calc.Core.Common;

namespace e3gbimtools.Calc.Abstractions
{
    public interface ICriticalPathService
    {
        /// <summary>Calcula el camino critico desde las filas de resultados.</summary>
        CriticalPathResult Analyze(IList<ResultRow> rows);
    }
}
