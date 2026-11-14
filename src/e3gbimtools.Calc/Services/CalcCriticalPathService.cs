// ============================================================================
//  CalcCriticalPathService.cs
//  Implementacion de ICriticalPathService.  Delega 1:1 en la clase
//  Core.Analytics.CriticalPathService (la migrada del monolito).
// ============================================================================

using System;
using System.Collections.Generic;
using E3G.BimTools.DiTools.Core.Abstractions;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Core.Common;
using Engine = e3gbimtools.Calc.Core.Analytics;

namespace e3gbimtools.Calc.Services
{
    public sealed class CalcCriticalPathService : ICriticalPathService
    {
        private readonly ILogger _log;
        private readonly Engine.CriticalPathService _engine = new Engine.CriticalPathService();

        public CalcCriticalPathService(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _log = loggerFactory.CreateLogger<CalcCriticalPathService>();
        }

        public Engine.CriticalPathResult Analyze(IList<ResultRow> rows)
        {
            try
            {
                _log.Debug("CriticalPath: analizando {Count} filas.", rows != null ? rows.Count : 0);
                return _engine.Analyze(rows);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "CriticalPath fallo durante el analisis.");
                throw;
            }
        }
    }
}
