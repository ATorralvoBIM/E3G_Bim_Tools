// ============================================================================
//  HeatmapService.cs
//  Calcula un brush por ResultRow en función de la métrica seleccionada.
//
//  Pipeline:
//    1) Extraer el valor numérico de la métrica para cada pipe.
//    2) Normalizar a [0, 1] vía min-max sobre el conjunto.
//    3) Mapear cada normalizado a un color con gradiente
//       verde → amarillo → rojo.
//
//  Convención de orientación:
//    Q, v, ΔP/m  → 0 = bajo (verde),  1 = alto (rojo).
//                  Resaltan los tramos con valores extremos.
//    Holgura     → 0 = sin margen (rojo), 1 = muy holgado (verde).
//                  Invertimos la escala para que "rojo = peligro" siga
//                  siendo la convención visual.
//
//  Estado de la métrica Holgura:
//    Se calcula como min(1 - v/vMax, 1 - ΔPpm/ΔPpmMax) usando límites
//    razonables por defecto (vMax=2 m/s, ΔPpmMax=400 Pa/m).  Si en el
//    futuro tenemos acceso a los SizingCriteria del cálculo concreto, se
//    pasan a Compute() vía un segundo parámetro y se usan en lugar de
//    los defaults.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ResultRow = e3gbimtools.Calc.Core.Common.ResultRow;

namespace e3gbimtools.Calc.Core.Analytics
{
    /// <summary>Resultado de aplicar el heatmap: brush + valor normalizado por fila.</summary>
    public sealed class HeatmapResult
    {
        public HeatmapMetric Metric { get; }
        public double Min { get; }
        public double Max { get; }
        public IReadOnlyDictionary<ResultRow, double> Values { get; }
        public IReadOnlyDictionary<ResultRow, Brush> Brushes { get; }

        public HeatmapResult(HeatmapMetric metric, double min, double max,
            IReadOnlyDictionary<ResultRow, double> values,
            IReadOnlyDictionary<ResultRow, Brush> brushes)
        {
            Metric = metric;
            Min = min;
            Max = max;
            Values = values;
            Brushes = brushes;
        }

        public bool IsEmpty => Brushes == null || Brushes.Count == 0;
    }

    public sealed class HeatmapService
    {
        /// <summary>Límites por defecto para la métrica "Holgura".</summary>
        public double DefaultVelocityMax { get; set; } = 2.0;       // m/s
        public double DefaultFrictionPaPmMax { get; set; } = 400.0; // Pa/m

        public HeatmapResult Compute(IList<ResultRow> rows, HeatmapMetric metric)
        {
            if (metric == HeatmapMetric.None || rows == null || rows.Count == 0)
                return Empty(metric);

            // 1) extraer valores
            var raw = new Dictionary<ResultRow, double>(rows.Count);
            foreach (var r in rows)
            {
                if (r == null || r.IsFitting) continue;
                raw[r] = ExtractValue(r, metric);
            }
            if (raw.Count == 0) return Empty(metric);

            double min = raw.Values.Min();
            double max = raw.Values.Max();
            double span = max - min;

            // 2) normalizar
            var normalized = new Dictionary<ResultRow, double>(raw.Count);
            foreach (var kv in raw)
            {
                double t = (span > 1e-9) ? (kv.Value - min) / span : 0.5;
                if (metric == HeatmapMetric.HoldingMargin) t = 1.0 - t; // invertir
                normalized[kv.Key] = t;
            }

            // 3) brushes
            var brushes = new Dictionary<ResultRow, Brush>(normalized.Count);
            foreach (var kv in normalized)
                brushes[kv.Key] = ColorForT(kv.Value);

            return new HeatmapResult(metric, min, max, raw, brushes);
        }

        private double ExtractValue(ResultRow r, HeatmapMetric metric)
        {
            switch (metric)
            {
                case HeatmapMetric.FlowLps:       return r.FlowLps;
                case HeatmapMetric.Velocity:      return r.Velocity;
                case HeatmapMetric.FrictionPaPm:  return r.FrictionPaPm;
                case HeatmapMetric.HoldingMargin: return ComputeHoldingMargin(r);
                default: return 0.0;
            }
        }

        private double ComputeHoldingMargin(ResultRow r)
        {
            double mV = (DefaultVelocityMax > 0 && r.Velocity > 0)
                ? Math.Max(0, 1.0 - r.Velocity / DefaultVelocityMax) : 1.0;
            double mF = (DefaultFrictionPaPmMax > 0 && r.FrictionPaPm > 0)
                ? Math.Max(0, 1.0 - r.FrictionPaPm / DefaultFrictionPaPmMax) : 1.0;
            return Math.Min(mV, mF);
        }

        /// <summary>
        /// Devuelve un Brush sobre el gradiente verde-amarillo-rojo
        /// (t ∈ [0,1]; 0 → verde, 0.5 → amarillo, 1 → rojo).
        /// </summary>
        public static Brush ColorForT(double t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            byte r, g, b;
            if (t < 0.5)
            {
                double tt = t * 2.0;
                r = (byte)(0x2E + (0xF9 - 0x2E) * tt);
                g = (byte)(0x7D + (0xA8 - 0x7D) * tt);
                b = (byte)(0x32 + (0x25 - 0x32) * tt);
            }
            else
            {
                double tt = (t - 0.5) * 2.0;
                r = (byte)(0xF9 + (0xC6 - 0xF9) * tt);
                g = (byte)(0xA8 + (0x28 - 0xA8) * tt);
                b = (byte)(0x25 + (0x28 - 0x25) * tt);
            }
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static HeatmapResult Empty(HeatmapMetric m)
            => new HeatmapResult(m, 0, 0,
                new Dictionary<ResultRow, double>(),
                new Dictionary<ResultRow, Brush>());
    }
}
