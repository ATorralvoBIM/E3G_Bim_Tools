// ============================================================================
//  HydraulicCalculator.cs
//  Core.Hydraulics — Motor de cálculo hidráulico.
//
//  Briefing técnico v1.0 — Mayo 2026, secciones 3.5 y 3.7.
//
//  Métodos públicos:
//    - CalculateSegment(seg, fluid)            : calcula v, Re, f, ΔP/m, ΔP total.
//    - DimensionSegment(flow, criterion, ...)  : itera DN hasta cumplir el criterio.
//    - SolveTreeNetwork(network, fluid)        : propaga caudales y presiones
//                                                desde la fuente.
//    - RunFullCalculation(network, fluid, cfg) : flujo completo + CalcReport.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using e3gbimtools.Calc.Core.Common;

namespace e3gbimtools.Calc.Core.Hydraulics
{
    /// <summary>
    /// Motor de cálculo hidráulico para redes de tuberías a presión.
    /// </summary>
    public class HydraulicCalculator
    {
        // -------------------------------------------------------------------
        //  Cálculo de un tramo aislado
        // -------------------------------------------------------------------

        /// <summary>
        /// Calcula velocidad, Reynolds, factor de fricción, pérdida lineal
        /// y total de un tramo a partir de su caudal y geometría.
        /// Modifica las propiedades del PipeSegment in-place.
        /// </summary>
        public CalcResult CalculateSegment(PipeSegment seg, FluidProperties fluid)
        {
            if (seg == null)   throw new ArgumentNullException(nameof(seg));
            if (fluid == null) throw new ArgumentNullException(nameof(fluid));

            var result = new CalcResult { RevitElementId = seg.RevitId };

            if (seg.DiameterM <= 0.0)
            {
                result.Warnings.Add(CalcWarning.Error("BAD_DIAMETER",
                    "Diámetro <= 0 en tramo.", seg.RevitId));
                return result;
            }

            double area = seg.AreaM2;
            double v    = (area > 0.0) ? seg.FlowM3s / area : 0.0;
            double re   = fluid.Density * Math.Abs(v) * seg.DiameterM / fluid.DynamicViscosity;
            double rough = (seg.Roughness > 0.0)
                ? seg.Roughness
                : MaterialDatabase.GetRoughness(seg.MaterialKey);
            double f = (re > 0.0)
                ? FrictionFactor.Calculate(re, rough, seg.DiameterM)
                : 0.0;
            double dpPerL = f * (1.0 / seg.DiameterM) * fluid.Density * v * v / 2.0;
            double singular = 0.0;
            for (int i = 0; i < seg.Fittings.Count; i++)
                singular += seg.Fittings[i].CalculateLossPa(Math.Abs(v), fluid.Density);
            double total = dpPerL * seg.LengthM + singular;

            seg.VelocityMs   = v;
            seg.FrictionPaPm = dpPerL;
            seg.TotalLossPa  = total;
            seg.Roughness    = rough;

            result.FlowM3s              = seg.FlowM3s;
            result.RawFlowM3s           = seg.RawFlowM3s;
            result.SimultaneityFactor   = seg.SimultaneityFactor;
            result.DownstreamFixtureCount = seg.DownstreamFixtureCount;
            result.VelocityMs           = v;
            result.ReynoldsNumber       = re;
            result.FrictionFactor       = f;
            result.FrictionPaPm         = dpPerL;
            result.LinearLossPa         = dpPerL * seg.LengthM;
            result.SingularLossPa       = singular;
            result.TotalLossPa          = total;
            result.LevelName            = seg.LevelName;
            result.BfsIndex             = seg.BfsIndex;
            result.FromNodeId           = seg.FromNodeId;
            result.ToNodeId             = seg.ToNodeId;
            return result;
        }

        // -------------------------------------------------------------------
        //  Dimensionado de un tramo según criterio
        // -------------------------------------------------------------------

        /// <summary>
        /// Itera la serie de diámetros nominales y devuelve la talla mínima
        /// que cumple el criterio (velocidad o pérdida unitaria).  Devuelve
        /// null si ninguna talla de la serie es suficiente.
        /// </summary>
        public NominalDiameter DimensionSegment(
            double flowM3s,
            SizingCriteria criterion,
            double maxValue,
            string materialKey,
            FluidProperties fluid,
            IReadOnlyList<NominalDiameter> series = null)
        {
            if (fluid == null) throw new ArgumentNullException(nameof(fluid));
            var s = series ?? NominalDiameters.SeriesForMaterial(materialKey);
            double rough = MaterialDatabase.GetRoughness(materialKey);

            foreach (var nd in s)
            {
                double D = nd.InnerDiameterM;
                if (D <= 0.0) continue;
                double a = Math.PI * D * D / 4.0;
                double v = Math.Abs(flowM3s) / a;

                if (criterion == SizingCriteria.MaxVelocity)
                {
                    if (v <= maxValue) return nd;
                    continue;
                }

                // MaxFriction o Economic (este último a v1 se trata como
                // MaxFriction con un valor objetivo bajo).
                double re = fluid.Density * v * D / fluid.DynamicViscosity;
                double f  = (re > 0.0) ? FrictionFactor.Calculate(re, rough, D) : 0.0;
                double dpPerL = f * (1.0 / D) * fluid.Density * v * v / 2.0;
                if (dpPerL <= maxValue) return nd;
            }
            return null;
        }

        // -------------------------------------------------------------------
        //  Resolución de red en árbol
        // -------------------------------------------------------------------

        /// <summary>
        /// Resuelve una red en árbol: propaga caudales desde los terminales
        /// hacia la fuente y luego calcula presiones desde la fuente hacia
        /// los terminales.  Modifica las propiedades de PipeSegment y
        /// PipeNode in-place.  Devuelve un CalcReport con el detalle.
        /// </summary>
        public CalcReport SolveTreeNetwork(PipeNetwork network, FluidProperties fluid,
            SizingCriteriaConfig config = null)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (fluid == null)   throw new ArgumentNullException(nameof(fluid));

            var report = new CalcReport
            {
                Title            = "Cálculo hidráulico de tuberías",
                SystemName       = network.SystemName,
                SourcePressurePa = network.SourcePressurePa,
            };

            if (network.SourceNode == null)
            {
                report.AddMessage(CalcWarning.Error("NO_SOURCE",
                    "La red no tiene nodo fuente definido."));
                return report;
            }

            var graph = network.ToGraph();
            if (graph.HasCycle())
            {
                report.AddMessage(CalcWarning.Error("CYCLE",
                    "La red contiene ciclos. Hardy-Cross no implementado en v1; se requiere árbol."));
                return report;
            }

            // 1) Caudales: para cada nodo, su demanda + suma de las demandas
            //    aguas abajo se acumula en el tramo de entrada.  Recorremos
            //    los tramos en orden topológico inverso.
            var topoEdges = graph.TopologicalSortEdges();
            if (topoEdges == null)
            {
                report.AddMessage(CalcWarning.Error("CYCLE",
                    "No se pudo ordenar topológicamente la red."));
                return report;
            }
            // Propagacion bottom-up: para cada tramo calculamos
            //  - RawFlowM3s = suma de demandas aguas-abajo (sin simultaneidad)
            //  - DownstreamFixtureCount = numero de aparatos con demanda aguas-abajo
            //  - SimultaneityFactor = K(n, perfil) segun config
            //  - FlowM3s = RawFlowM3s * K  (caudal de diseno)
            var profile = config?.SimultaneityProfile ?? SimultaneityProfile.None;
            for (int i = topoEdges.Count - 1; i >= 0; i--)
            {
                var seg = topoEdges[i];
                double rawQ = 0.0;
                int fxCount = 0;
                if (seg.NodeEnd != null)
                {
                    if (seg.NodeEnd.DemandM3s > 0.0)
                    {
                        rawQ += seg.NodeEnd.DemandM3s;
                        fxCount += 1;
                    }
                    foreach (var s2 in seg.NodeEnd.ConnectedSegments)
                    {
                        if (ReferenceEquals(s2, seg)) continue;
                        if (ReferenceEquals(s2.NodeStart, seg.NodeEnd))
                        {
                            rawQ    += s2.RawFlowM3s;
                            fxCount += s2.DownstreamFixtureCount;
                        }
                    }
                }
                seg.RawFlowM3s              = rawQ;
                seg.DownstreamFixtureCount  = fxCount;
                seg.SimultaneityFactor      = SizingCriteriaConfig.SimultaneityFactorOf(fxCount, profile);
                seg.FlowM3s                 = rawQ * seg.SimultaneityFactor;
            }

            // 1.5) Conectividad: localizar el TRAMO EXACTO de cada corte.
            //      reachable = nudos alcanzables desde la fuente (BFS dirigido,
            //      válido porque OrientFromSource orientó el componente).
            var reachable = new HashSet<PipeNode>();
            foreach (var nd in graph.Bfs(network.SourceNode)) reachable.Add(nd);

            var breaks = ConnectivityAnalyzer.FindBreaks(
                network.Nodes, network.Segments, reachable,
                n => n.ConnectedSegments,
                e => e.NodeStart, e => e.NodeEnd, e => e.RevitId,
                n => n.X, n => n.Y, n => n.ElevationM);

            // 2) Pérdidas por tramo
            foreach (var seg in network.Segments)
            {
                var r = CalculateSegment(seg, fluid);
                if (breaks.BreakSegmentIds.Contains(seg.RevitId))
                {
                    r.Disconnected = true;   // tramo EXACTO del corte
                    r.Warnings.Add(CalcWarning.Error("DISCONNECTED",
                        "CORTE en este tramo: aquí la red pierde la conexión con la "
                        + "fuente (un codo/te/unión sin conectar o un hueco entre tubos). "
                        + "Revisa y reconecta este punto.",
                        seg.RevitId));
                }
                else if (breaks.DownstreamSegmentIds.Contains(seg.RevitId))
                {
                    r.Warnings.Add(CalcWarning.Warn("DOWNSTREAM_OF_BREAK",
                        "Aguas abajo de un corte: este tramo queda aislado de la fuente; "
                        + "su resultado no es fiable hasta reconectar el corte.",
                        seg.RevitId));
                }
                report.AddSegment(r);
            }

            if (breaks.BreakSegmentIds.Count > 0)
            {
                report.AddMessage(CalcWarning.Error("DISCONNECTED_NET",
                    breaks.BreakSegmentIds.Count + " corte(s) detectados en la red"
                    + (breaks.NearestGapM > 0.0
                        ? " (hueco mínimo ≈ " + (breaks.NearestGapM * 1000.0).ToString("0") + " mm)"
                        : "")
                    + ". Usa «Localizar desconectados» para ir al tramo exacto."));
            }

            // 3) Presiones: BFS desde la fuente.  P(end) = P(start) - ΔP_total
            //    + correccion por cota (presión estática).
            network.SourceNode.PressurePa = network.SourcePressurePa;
            double rhoG = fluid.Density * 9.80665;
            var bfsEdges = graph.BreadthFirstOrderEdges();
            foreach (var seg in bfsEdges)
            {
                if (seg.NodeStart == null || seg.NodeEnd == null) continue;
                double dz = seg.NodeEnd.ElevationM - seg.NodeStart.ElevationM;
                seg.NodeEnd.PressurePa = seg.NodeStart.PressurePa - seg.TotalLossPa - rhoG * dz;
            }

            // 4) Métricas del informe
            double minP = double.PositiveInfinity;
            double worstDrop = 0.0;
            foreach (var t in network.TerminalNodes)
            {
                if (t.PressurePa < minP) minP = t.PressurePa;
                double drop = network.SourcePressurePa - t.PressurePa;
                if (drop > worstDrop) worstDrop = drop;
            }
            report.MinTerminalPressurePa   = double.IsPositiveInfinity(minP) ? 0.0 : minP;
            report.WorstCasePressureDropPa = worstDrop;
            return report;
        }

        // -------------------------------------------------------------------
        //  Flujo completo (cálculo + criterios)
        // -------------------------------------------------------------------

        /// <summary>
        /// Resuelve la red y, opcionalmente, marca tramos que excedan los
        /// criterios de velocidad o pérdida unitaria del SizingCriteriaConfig.
        /// </summary>
        public CalcReport RunFullCalculation(
            PipeNetwork network,
            FluidProperties fluid,
            SizingCriteriaConfig criteria = null)
        {
            var report = SolveTreeNetwork(network, fluid, criteria);
            if (criteria == null) return report;

            // Itera por seg para tener acceso a MaterialKey y FlowM3s (Q de
            // diseno con simultaneidad ya aplicada).
            double limit = (criteria.Criterion == SizingCriteria.MaxVelocity)
                ? criteria.MaxVelocityMs
                : criteria.MaxFrictionPaPm;

            foreach (var seg in network.Segments)
            {
                if (!report.Segments.TryGetValue(seg.RevitId, out var r)) continue;

                // Chequeos de criterio existentes
                if (criteria.MaxVelocityMs > 0 && Math.Abs(r.VelocityMs) > criteria.MaxVelocityMs)
                {
                    r.VelocityExceeded = true;
                    r.Warnings.Add(CalcWarning.Warn("VEL_EXCEEDED",
                        "Velocidad " + r.VelocityMs.ToString("0.00") + " m/s supera el límite "
                        + criteria.MaxVelocityMs.ToString("0.00") + " m/s.",
                        r.RevitElementId));
                }
                if (criteria.MaxFrictionPaPm > 0 && r.FrictionPaPm > criteria.MaxFrictionPaPm)
                {
                    r.FrictionExceeded = true;
                    r.Warnings.Add(CalcWarning.Warn("FRICTION_EXCEEDED",
                        "ΔP/m " + r.FrictionPaPm.ToString("0") + " Pa/m supera el límite "
                        + criteria.MaxFrictionPaPm.ToString("0") + " Pa/m.",
                        r.RevitElementId));
                }

                // DN recomendado: talla minima que cumple el criterio.  Se usa
                // el CATALOGO real del tipo de tuberia en Revit si esta
                // disponible; en su defecto, la serie normativa por material.
                NominalDiameter recom = null;
                if (seg.FlowM3s > 0.0 && limit > 0.0 && !string.IsNullOrEmpty(seg.MaterialKey))
                {
                    var series = (seg.CatalogSeries != null && seg.CatalogSeries.Count > 0)
                        ? seg.CatalogSeries
                        : null;
                    recom = DimensionSegment(
                        Math.Abs(seg.FlowM3s),
                        criteria.Criterion,
                        limit,
                        seg.MaterialKey,
                        fluid,
                        series);
                    if (recom != null)
                    {
                        r.RecommendedSize     = recom.Label;
                        r.RecommendedNominalM = recom.Dn / 1000.0;
                    }
                }

                // Velocidad por debajo del mínimo => tubería sobredimensionada.
                // Solo se marca como incumplimiento si:
                //   - hay caudal real en el tramo, y
                //   - existe una talla menor (recomendada) a la que reducir.
                // Si la tubería ya es la más pequeña disponible que cumple, la
                // baja velocidad es físicamente inevitable y NO se marca.
                if (criteria.MinVelocityMs > 0 && Math.Abs(seg.FlowM3s) > 1e-9
                    && Math.Abs(r.VelocityMs) < criteria.MinVelocityMs && !r.VelocityExceeded
                    && recom != null && (recom.Dn / 1000.0) < seg.DiameterM - 5e-4)
                {
                    r.VelocityBelowMin = true;
                    r.Warnings.Add(CalcWarning.Warn("VEL_BELOW_MIN",
                        "Velocidad " + r.VelocityMs.ToString("0.00") + " m/s por debajo del mínimo "
                        + criteria.MinVelocityMs.ToString("0.00") + " m/s (tubería sobredimensionada).",
                        r.RevitElementId));
                }
            }
            return report;
        }
    }
}
