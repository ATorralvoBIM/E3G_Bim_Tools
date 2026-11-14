// ============================================================================
//  CriticalPathResult.cs
//  DTO inmutable que devuelve CriticalPathService.Analyze().
// ============================================================================
using System;
using System.Collections.Generic;
using ResultRow = e3gbimtools.Calc.Core.Common.ResultRow;

namespace e3gbimtools.Calc.Core.Analytics
{
    public sealed class CriticalPathResult
    {
        /// <summary>
        /// Lista ordenada de tramos que forman el camino crítico, desde la
        /// fuente (primer elemento) hasta el terminal peor-caso (último).
        /// </summary>
        public IReadOnlyList<ResultRow> Path { get; }

        /// <summary>Tramo final del camino — termina en el terminal crítico.</summary>
        public ResultRow Terminal { get; }

        /// <summary>ΔP acumulado en el terminal crítico, en Pa.</summary>
        public double TotalDeltaP { get; }

        /// <summary>
        /// ΔP acumulado en cada tramo del grafo, NO solo en el camino crítico.
        /// Útil para renderizar etiquetas en cualquier nudo o para análisis
        /// posteriores (segundo-peor, tercero-peor, etc.).
        /// </summary>
        public IReadOnlyDictionary<ResultRow, double> AccumulatedAt { get; }

        public CriticalPathResult(
            IReadOnlyList<ResultRow> path,
            ResultRow terminal,
            double totalDeltaP,
            IReadOnlyDictionary<ResultRow, double> accumulatedAt)
        {
            Path = path ?? Array.Empty<ResultRow>();
            Terminal = terminal;
            TotalDeltaP = totalDeltaP;
            AccumulatedAt = accumulatedAt ?? new Dictionary<ResultRow, double>();
        }

        public bool IsEmpty => Path == null || Path.Count == 0;
    }
}
