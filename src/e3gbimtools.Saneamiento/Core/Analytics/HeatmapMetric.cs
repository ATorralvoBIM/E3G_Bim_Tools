// ============================================================================
//  HeatmapMetric.cs
//  Enumeración de métricas que el heatmap puede pintar sobre el esquema.
// ============================================================================
namespace E3G.Saneamiento.Core.Analytics
{
    public enum HeatmapMetric
    {
        /// <summary>Sin heatmap — el esquema pinta el color del sistema.</summary>
        None,
        /// <summary>Caudal de diseño en L/s.</summary>
        FlowLps,
        /// <summary>Velocidad en m/s.</summary>
        Velocity,
        /// <summary>Pérdida unitaria en Pa/m.</summary>
        FrictionPaPm,
        /// <summary>
        /// Holgura: cuán cerca está el tramo de los límites del criterio.
        /// 0 = al límite o por encima → ROJO; 1 = muy holgado → VERDE.
        /// </summary>
        HoldingMargin,
    }
}
