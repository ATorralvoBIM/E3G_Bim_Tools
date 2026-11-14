// ============================================================================
//  CalcSchematicLayoutService.cs
//  Implementacion de ISchematicLayoutService.  Delega 1:1 en
//  Core.Schematic.SchematicLayoutEngine.
// ============================================================================

using System;
using System.Collections.Generic;
using E3G.BimTools.DiTools.Core.Abstractions;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Schematic;

namespace e3gbimtools.Calc.Services
{
    public sealed class CalcSchematicLayoutService : ISchematicLayoutService
    {
        private readonly ILogger _log;
        private readonly SchematicLayoutEngine _engine = new SchematicLayoutEngine();

        public CalcSchematicLayoutService(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _log = loggerFactory.CreateLogger<CalcSchematicLayoutService>();
        }

        public SchematicLayout Build(
            IList<ResultRow> rows,
            IReadOnlyDictionary<ResultRow, int> columnOf,
            SchematicParameters parameters,
            ISet<ResultRow> criticalPathRows = null,
            SchematicLegend legend = null)
        {
            try
            {
                _log.Debug("SchematicLayout: filas={Count}.", rows != null ? rows.Count : 0);
                return _engine.Build(rows, columnOf, parameters, criticalPathRows, legend);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "SchematicLayout fallo en Build.");
                throw;
            }
        }
    }
}
