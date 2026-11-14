// ============================================================================
//  AeraulicCalculator.cs
//  Core.Aeraulics — Motor de cálculo aerodinámico de conductos.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 4.
//  Reusa Core.Hydraulics.FrictionFactor (Darcy + Colebrook-White) sobre el
//  diametro hidraulico del conducto.
//
//  Métodos públicos:
//    - CalculateSegment(seg, fluid)           : v, Re, f, dP/m, dP total.
//    - DimensionCircularByVelocity(...)       : itera serie circular hasta v <= v_max.
//    - DimensionCircularByEqualFriction(...)  : itera serie circular hasta dP/m <= max.
//    - DimensionRectangularByVelocity(...)    : itera serie rectangular hasta v <= v_max.
//    - SolveTreeNetwork(network, fluid)       : propaga caudales y presiones.
//    - RunFullCalculation(network, fluid, cfg): cálculo + chequeo de criterios.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using E3G.Saneamiento.Core.Common;
using E3G.Saneamiento.Core.Hydraulics;

namespace E3G.Saneamiento.Core.Aeraulics
{
    /// <summary>Motor aeráulico para redes de conductos.</summary>
    public class AeraulicCalculator
    {
        // -------------------------------------------------------------------
        //  Cálculo de un tramo aislado
        // -------------------------------------------------------------------

        /// <summary>
        /// Calcula velocidad, Reynolds, factor de fricción, dP/m y dP total
        /// de un tramo aplicando Darcy-Weisbach con el diametro hidraulico.
        /// </summary>
        public CalcResult CalculateSegment(DuctSegment seg, FluidProperties fluid)
        {
            if (seg == null)   throw new ArgumentNullException(nameof(seg));
            if (fluid == null) throw new ArgumentNullException(nameof(fluid));

            var result = new CalcResult { RevitElementId = seg.RevitId };

            double area = seg.AreaM2;
            double dh   = seg.HydraulicDiameterM;
            if (area <= 0.0 || dh <= 0.0)
            {
                result.Warnings.Add(CalcWarning.Error("BAD_GEOMETRY",
                    "Geometria invalida (area o D_h <= 0).", seg.RevitId));
                return result;
            }

            double v    = seg.FlowM3s / area;
            double re   = fluid.Density * Math.Abs(v) * dh / fluid.DynamicViscosity;
            double f    = (re > 0.0)
                ? FrictionFactor.Calculate(re, seg.Roughness, dh)
                : 0.0;
            double dpPerL = f * (1.0 / dh) * fluid.Density * v * v / 2.0;
            double singular = 0.0;
            for (int i = 0; i < seg.Fittings.Count; i++)
                singular += seg.Fittings[i].CalculateLossPa(Math.Abs(v), fluid.Density);
            double total = dpPerL * seg.LengthM + singular;

            seg.VelocityMs   = v;
            seg.FrictionPaPm = dpPerL;
            seg.TotalLossPa  = total;

            result.FlowM3s        = seg.FlowM3s;
            result.VelocityMs     = v;
            result.ReynoldsNumber = re;
            result.FrictionFactor = f;
            result.FrictionPaPm   = dpPerL;
            result.LinearLossPa   = dpPerL * seg.LengthM;
            result.SingularLossPa = singular;
            result.TotalLossPa    = total;
            return result;
        }

        // -------------------------------------------------------------------
        //  Dimensionado por velocidad maxima (circular y rectangular)
        // -------------------------------------------------------------------

        /// <summary>
        /// Talla circular minima de la serie EN 1506 con v <= maxVelocityMs.
        /// </summary>
        public CircularDuctSize DimensionCircularByVelocity(double flowM3s, double maxVelocityMs)
        {
            if (flowM3s <= 0.0 || maxVelocityMs <= 0.0) return null;
            double aMin = flowM3s / maxVelocityMs;
            double dMin = Math.Sqrt(4.0 * aMin / Math.PI);
            return DuctSizingSeries.NextUpCircular(dMin);
        }

        /// <summary>
        /// Talla rectangular mínima de la serie SMACNA con v <= maxVelocityMs.
        /// Itera por orden de superficie creciente.
        /// </summary>
        public RectangularDuctSize DimensionRectangularByVelocity(double flowM3s, double maxVelocityMs)
        {
            if (flowM3s <= 0.0 || maxVelocityMs <= 0.0) return null;
            double aMin = flowM3s / maxVelocityMs;
            return DuctSizingSeries.NextUpRectangular(aMin);
        }

        // -------------------------------------------------------------------
        //  Dimensionado por igual fricción (circular)
        // -------------------------------------------------------------------

        /// <summary>
        /// Talla circular mínima con dP/m <= maxPaPerM.  Itera la serie EN 1506
        /// en orden creciente y devuelve la primera que cumple.
        /// </summary>
        public CircularDuctSize DimensionCircularByEqualFriction(
            double flowM3s, double maxPaPerM, FluidProperties fluid, double roughness = 0.0001)
        {
            if (flowM3s <= 0.0 || maxPaPerM <= 0.0 || fluid == null) return null;
            foreach (var c in DuctSizingSeries.Circular)
            {
                double D = c.DiameterM;
                double a = c.AreaM2;
                double v = flowM3s / a;
                double re = fluid.Density * v * D / fluid.DynamicViscosity;
                if (re <= 0.0) continue;
                double f = FrictionFactor.Calculate(re, roughness, D);
                double dp = f * (1.0 / D) * fluid.Density * v * v / 2.0;
                if (dp <= maxPaPerM) return c;
            }
            return DuctSizingSeries.Circular.Count > 0
                ? DuctSizingSeries.Circular[DuctSizingSeries.Circular.Count - 1]
                : null;
        }

        // -------------------------------------------------------------------
        //  Resolución de red en árbol
        // -------------------------------------------------------------------

        /// <summary>
        /// Resuelve una red en árbol: caudales (terminales -> fuente) y
        /// presiones totales (fuente -> terminales).  Modifica los tramos
        /// y nudos in-place y devuelve un CalcReport.
        /// </summary>
        public CalcReport SolveTreeNetwork(DuctNetwork network, FluidProperties fluid)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (fluid == null)   throw new ArgumentNullException(nameof(fluid));

            var report = new CalcReport
            {
                Title            = "Calculo aeraulico de conductos",
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
                    "Red con ciclo. v1 solo soporta arbol; conductos en lazo no implementado."));
                return report;
            }

            var topoEdges = graph.TopologicalSortEdges();
            if (topoEdges == null)
            {
                report.AddMessage(CalcWarning.Error("CYCLE",
                    "No se pudo ordenar topologicamente la red."));
                return report;
            }

            // 1) Caudales propagados hacia la fuente
            for (int i = topoEdges.Count - 1; i >= 0; i--)
            {
                var seg = topoEdges[i];
                double q = seg.NodeEnd != null ? seg.NodeEnd.DemandM3s : 0.0;
                if (seg.NodeEnd != null)
                {
                    foreach (var s2 in seg.NodeEnd.ConnectedSegments)
                    {
                        if (ReferenceEquals(s2, seg)) continue;
                        if (ReferenceEquals(s2.NodeStart, seg.NodeEnd))
                            q += s2.FlowM3s;
                    }
                }
                seg.FlowM3s = q;
            }

            // 2) Pérdidas por tramo
            foreach (var seg in network.Segments)
            {
                var r = CalculateSegment(seg, fluid);
                report.AddSegment(r);
            }

            // 3) Presiones totales: BFS desde la fuente.  P_t(end) = P_t(start) - dP_total.
            //    Para conductos la cota apenas afecta (rho aire baja); se incluye
            //    igualmente para coherencia con el modelo hidraulico.
            network.SourceNode.TotalPressurePa = network.SourcePressurePa;
            double rhoG = fluid.Density * AirProperties.Gravity;
            var bfsEdges = graph.BreadthFirstOrderEdges();
            foreach (var seg in bfsEdges)
            {
                if (seg.NodeStart == null || seg.NodeEnd == null) continue;
                double dz = seg.NodeEnd.ElevationM - seg.NodeStart.ElevationM;
                seg.NodeEnd.TotalPressurePa =
                    seg.NodeStart.TotalPressurePa - seg.TotalLossPa - rhoG * dz;
            }

            // 4) Métricas
            double minP = double.PositiveInfinity;
            double worstDrop = 0.0;
            foreach (var t in network.TerminalNodes)
            {
                if (t.TotalPressurePa < minP) minP = t.TotalPressurePa;
                // El peor caso incluye la perdida en el terminal (rejilla / difusor)
                // ya que la fuente debe vencer tambien esa caida final.
                double drop = network.SourcePressurePa - t.TotalPressurePa + t.TerminalPressureDropPa;
                if (drop > worstDrop) worstDrop = drop;
            }
            report.MinTerminalPressurePa   = double.IsPositiveInfinity(minP) ? 0.0 : minP;
            report.WorstCasePressureDropPa = worstDrop;
            return report;
        }

        // -------------------------------------------------------------------
        //  Flujo completo
        // -------------------------------------------------------------------

        /// <summary>
        /// Resuelve la red y marca tramos que excedan los criterios definidos
        /// en el SizingCriteriaConfig.
        /// </summary>
        public CalcReport RunFullCalculation(
            DuctNetwork network,
            FluidProperties fluid,
            SizingCriteriaConfig criteria = null)
        {
            var report = SolveTreeNetwork(network, fluid);
            if (criteria == null) return report;

            // Itera por seg para tener acceso a Shape, FlowM3s, Roughness y
            // poder dimensionar (talla recomendada por la serie circular o
            // rectangular segun el criterio del SizingCriteriaConfig).
            foreach (var seg in network.Segments)
            {
                if (!report.Segments.TryGetValue(seg.RevitId, out var r)) continue;

                if (criteria.MaxVelocityMs > 0 && Math.Abs(r.VelocityMs) > criteria.MaxVelocityMs)
                {
                    r.VelocityExceeded = true;
                    r.Warnings.Add(CalcWarning.Warn("VEL_EXCEEDED",
                        "Velocidad " + r.VelocityMs.ToString("0.00") + " m/s supera el limite "
                        + criteria.MaxVelocityMs.ToString("0.00") + " m/s.",
                        r.RevitElementId));
                }
                if (criteria.MaxFrictionPaPm > 0 && r.FrictionPaPm > criteria.MaxFrictionPaPm)
                {
                    r.FrictionExceeded = true;
                    r.Warnings.Add(CalcWarning.Warn("FRICTION_EXCEEDED",
                        "dP/m " + r.FrictionPaPm.ToString("0.00") + " Pa/m supera el limite "
                        + criteria.MaxFrictionPaPm.ToString("0.00") + " Pa/m.",
                        r.RevitElementId));
                }

                // Talla recomendada segun forma del conducto + criterio
                if (seg.FlowM3s > 0.0)
                {
                    if (seg.Shape == DuctShape.Circular)
                    {
                        CircularDuctSize recom = null;
                        if (criteria.Criterion == SizingCriteria.MaxFriction
                            && criteria.MaxFrictionPaPm > 0)
                            recom = DimensionCircularByEqualFriction(
                                Math.Abs(seg.FlowM3s), criteria.MaxFrictionPaPm,
                                fluid, seg.Roughness > 0 ? seg.Roughness : 0.0001);
                        else if (criteria.MaxVelocityMs > 0)
                            recom = DimensionCircularByVelocity(
                                Math.Abs(seg.FlowM3s), criteria.MaxVelocityMs);
                        if (recom != null) r.RecommendedSize = recom.Label;
                    }
                    else if (seg.Shape == DuctShape.Rectangular)
                    {
                        RectangularDuctSize recom = null;
                        if (criteria.MaxVelocityMs > 0)
                            recom = DimensionRectangularByVelocity(
                                Math.Abs(seg.FlowM3s), criteria.MaxVelocityMs);
                        if (recom != null) r.RecommendedSize = recom.Label;
                    }
                }
            }
            return report;
        }
    }
}
