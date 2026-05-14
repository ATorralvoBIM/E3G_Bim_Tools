// ============================================================================
//  CalcSelfTest.cs
//  Tests/  — Smoke-tests del motor de cálculo MEP. Ejecutables sin NUnit.
//
//  Esta clase NO pertenece a Core.* — vive en E3G.Saneamiento.Tests para
//  poder referenciar libremente Core.Common, Core.Hydraulics, Core.Aeraulics
//  y RevitBridge.  La capa Core sigue siendo cero-dependencia.
//
//  Lanzado desde Revit por el comando CmdMepCalcSelfTest (botón en el ribbon).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using E3G.Saneamiento.Core.Common;
using E3G.Saneamiento.Core.Hydraulics;
using E3G.Saneamiento.Core.Aeraulics;
using E3G.Saneamiento.RevitBridge;

namespace E3G.Saneamiento.Tests
{
    public static class CalcSelfTest
    {
        public class Report
        {
            public int    Total      { get; set; }
            public int    Passed     { get; set; }
            public int    Failed     { get; set; }
            public string FullText   { get; set; }
            public bool   AllPassed  => Failed == 0;
        }

        public static Report Run()
        {
            var sb     = new StringBuilder();
            int total  = 0;
            int passed = 0;

            sb.AppendLine("E3G BIM Tools - MEP Calc Self-Test (Sprint 1..6 + 8 validation)");
            sb.AppendLine("================================================================");
            sb.AppendLine();

            // =====================================================================
            //  Sprint 1 - Core.Common
            // =====================================================================

            sb.AppendLine("[Units]");
            Check(sb, ref total, ref passed,
                "ft -> m round-trip",
                () => Approx(Units.MToFt(Units.FtToM(10.0)), 10.0, 1e-9));
            Check(sb, ref total, ref passed,
                "in -> m (1 in = 0.0254 m)",
                () => Approx(Units.InToM(1.0), 0.0254, 1e-9));
            Check(sb, ref total, ref passed,
                "gpm -> m3/s (1 gpm = 6.30902e-5 m3/s)",
                () => Approx(Units.GpmToM3s(1.0), 6.30901964e-5, 1e-10));
            Check(sb, ref total, ref passed,
                "CFM -> m3/s (1 CFM = 4.71947e-4 m3/s)",
                () => Approx(Units.CfmToM3s(1.0), 4.71947e-4, 1e-7));
            Check(sb, ref total, ref passed,
                "Pa <-> inH2O round-trip",
                () => Approx(Units.PaToInWc(Units.InWcToPa(2.5)), 2.5, 1e-9));
            Check(sb, ref total, ref passed,
                "Pa/m <-> inH2O/100ft round-trip",
                () => Approx(Units.PaPerMToInWcPer100Ft(Units.InWcPer100FtToPaPerM(0.5)), 0.5, 1e-9));
            Check(sb, ref total, ref passed,
                "RequirePositive lanza con 0",
                () => { try { Units.RequirePositive(0.0, "x"); return false; }
                        catch (ArgumentOutOfRangeException) { return true; } });
            sb.AppendLine();

            sb.AppendLine("[FluidProperties]");
            var w20 = FluidProperties.Water20C;
            Check(sb, ref total, ref passed, "Agua 20 C: rho ~ 998.2 kg/m3",
                () => Approx(w20.Density, 998.2, 0.5));
            Check(sb, ref total, ref passed, "Agua 20 C: mu ~ 1.002e-3 Pa.s",
                () => Approx(w20.DynamicViscosity, 1.002e-3, 5e-5));
            Check(sb, ref total, ref passed, "Agua 20 C: nu ~ 1.004e-6 m2/s",
                () => Approx(w20.KinematicViscosity, 1.004e-6, 5e-8));
            Check(sb, ref total, ref passed, "Agua 20 C: cp ~ 4182 J/(kg.K) (+/-15)",
                () => Approx(w20.SpecificHeat, 4182.0, 15.0));
            Check(sb, ref total, ref passed, "Agua 60 C: rho ~ 983 kg/m3",
                () => Approx(FluidProperties.Water60C.Density, 983.2, 1.0));
            Check(sb, ref total, ref passed, "Agua 80 C: mu ~ 0.354e-3 Pa.s",
                () => Approx(FluidProperties.Water80C.DynamicViscosity, 0.354e-3, 5e-5));
            Check(sb, ref total, ref passed, "PG 30% 20 C: rho > agua",
                () => FluidProperties.PG30(20.0).Density > w20.Density);
            Check(sb, ref total, ref passed, "PG 50% 20 C: mu > 3*mu_agua",
                () => FluidProperties.PG50(20.0).DynamicViscosity > 3.0 * w20.DynamicViscosity);
            Check(sb, ref total, ref passed, "Aire 20 C: rho ~ 1.204 kg/m3",
                () => Approx(FluidProperties.Air20C.Density, 1.204, 0.005));
            sb.AppendLine();

            sb.AppendLine("[NetworkGraph]");
            var nA = new Nd("A"); var nB = new Nd("B"); var nC = new Nd("C"); var nD = new Nd("D");
            var eAB = new Eg(nA, nB); var eAC = new Eg(nA, nC); var eBD = new Eg(nB, nD);
            var g = new NetworkGraph<Nd, Eg>
            {
                GetEdgeStart = e => e.S, GetEdgeEnd = e => e.E, SourceNode = nA,
            };
            g.AddEdge(eAB); g.AddEdge(eAC); g.AddEdge(eBD);
            Check(sb, ref total, ref passed, "Roots() == { A }",
                () => g.Roots().Count() == 1 && g.Roots().First() == nA);
            Check(sb, ref total, ref passed, "Leaves() == { C, D }",
                () => { var l = g.Leaves().ToList(); return l.Count == 2 && l.Contains(nC) && l.Contains(nD); });
            Check(sb, ref total, ref passed, "BFS desde A visita 4 nodos",
                () => g.Bfs(nA).Count() == 4);
            Check(sb, ref total, ref passed, "TopologicalSortNodes valido (A antes que D)",
                () => { var o = g.TopologicalSortNodes(); return o != null
                    && o.IndexOf(nA) < o.IndexOf(nB) && o.IndexOf(nB) < o.IndexOf(nD); });
            Check(sb, ref total, ref passed, "HasCycle() == false en arbol",
                () => !g.HasCycle());
            Check(sb, ref total, ref passed, "BreadthFirstOrderEdges devuelve 3 aristas",
                () => g.BreadthFirstOrderEdges().Count == 3);
            var c1 = new Nd("c1"); var c2 = new Nd("c2"); var c3 = new Nd("c3");
            var gc = new NetworkGraph<Nd, Eg> { GetEdgeStart = e => e.S, GetEdgeEnd = e => e.E };
            gc.AddEdge(new Eg(c1, c2)); gc.AddEdge(new Eg(c2, c3)); gc.AddEdge(new Eg(c3, c1));
            Check(sb, ref total, ref passed, "HasCycle() == true en triangulo dirigido",
                () => gc.HasCycle());
            sb.AppendLine();

            sb.AppendLine("[CalcResult / CalcReport]");
            var rTest = new CalcResult
            {
                RevitElementId = 12345L, FlowM3s = 0.001, VelocityMs = 1.2,
                FrictionPaPm = 150.0, LinearLossPa = 1500.0, SingularLossPa = 200.0,
                TotalLossPa = 1700.0, ReynoldsNumber = 25000.0, FrictionFactor = 0.025,
                RecommendedSize = "DN50",
            };
            Check(sb, ref total, ref passed, "CalcResult sin warnings -> IsOk == true",
                () => rTest.IsOk);
            rTest.Warnings.Add(CalcWarning.Error("X", "fallo simulado", 12345L));
            Check(sb, ref total, ref passed, "CalcResult con error -> IsOk == false",
                () => !rTest.IsOk);
            var report1 = new CalcReport { SystemName = "Test", SourcePressurePa = 300000 };
            report1.AddSegment(rTest);
            report1.AddMessage(CalcWarning.Warn("W1", "warning global"));
            Check(sb, ref total, ref passed, "CalcReport: 1 segmento + 1 warning + 1 error",
                () => report1.Segments.Count == 1 && report1.CountWarnings >= 1 && report1.CountErrors >= 1);
            Check(sb, ref total, ref passed, "CalcReport.Success == false (hay error)",
                () => !report1.Success);
            sb.AppendLine();

            sb.AppendLine("[SizingCriteria]");
            Check(sb, ref total, ref passed, "Preset CTE_AF carga sin lanzar",
                () => SizingCriteriaConfig.FromPreset("CTE_AF") != null);
            Check(sb, ref total, ref passed, "Preset CTE_ACS: v_max == 1.5 m/s",
                () => Approx(SizingCriteriaConfig.FromPreset("CTE_ACS").MaxVelocityMs, 1.5, 1e-9));
            Check(sb, ref total, ref passed, "Preset RITE_HEATING: criterio == MaxFriction",
                () => SizingCriteriaConfig.FromPreset("RITE_HEATING").Criterion == SizingCriteria.MaxFriction);
            Check(sb, ref total, ref passed, "Preset desconocido -> KeyNotFoundException",
                () => { try { SizingCriteriaConfig.FromPreset("NO_EXISTE"); return false; }
                        catch (KeyNotFoundException) { return true; } });
            Check(sb, ref total, ref passed, "Clone() devuelve copia independiente",
                () =>
                {
                    var orig = SizingCriteriaConfig.FromPreset("CTE_AF");
                    var clone = orig.Clone();
                    clone.MaxVelocityMs = 99.0;
                    return Math.Abs(orig.MaxVelocityMs - 99.0) > 1e-6;
                });
            sb.AppendLine();

            // =====================================================================
            //  Sprint 2 - Core.Hydraulics
            // =====================================================================

            sb.AppendLine("[FrictionFactor]");
            Check(sb, ref total, ref passed, "Laminar Re=1000: f = 64/Re = 0.064",
                () => Approx(FrictionFactor.Calculate(1000.0, 0.000150, 0.05), 0.064, 1e-9));
            Check(sb, ref total, ref passed, "Laminar Re=2000: f = 0.032",
                () => Approx(FrictionFactor.Calculate(2000.0, 0.000150, 0.05), 0.032, 1e-9));
            Check(sb, ref total, ref passed, "Turbulento DN50 acero galv Re=25000: 0.025 < f < 0.040",
                () => { double f = FrictionFactor.Calculate(25000.0, 0.000150, 0.0531); return f > 0.025 && f < 0.040; });
            Check(sb, ref total, ref passed, "Tuberia lisa Re=1e5: 0.018 < f < 0.022",
                () => { double f = FrictionFactor.Calculate(1e5, 0.0, 0.05); return f > 0.016 && f < 0.022; });
            Check(sb, ref total, ref passed, "Transitorio Re=3000: 0.025 < f < 0.064",
                () => { double f = FrictionFactor.Calculate(3000.0, 0.000150, 0.0531); return f > 0.025 && f < 0.064; });
            Check(sb, ref total, ref passed, "Re=0 -> NaN",
                () => double.IsNaN(FrictionFactor.Calculate(0.0, 0.000150, 0.05)));
            sb.AppendLine();

            sb.AppendLine("[MaterialDatabase]");
            Check(sb, ref total, ref passed, "STEEL_GALV: epsilon = 1.5e-4 m",
                () => Approx(MaterialDatabase.GetRoughness("STEEL_GALV"), 0.000150, 1e-9));
            Check(sb, ref total, ref passed, "COPPER: epsilon = 1.5e-6 m",
                () => Approx(MaterialDatabase.GetRoughness("COPPER"), 0.0000015, 1e-12));
            Check(sb, ref total, ref passed, "Material desconocido -> default (acero galv)",
                () => Approx(MaterialDatabase.GetRoughness("XXX_NONEXISTENT"), 0.000150, 1e-9));
            Check(sb, ref total, ref passed, "ResolveFromRevitName(\"Galvanized Steel\") -> STEEL_GALV",
                () => MaterialDatabase.ResolveFromRevitName("Galvanized Steel") == "STEEL_GALV");
            Check(sb, ref total, ref passed, "Contains(\"PVC\") == true",
                () => MaterialDatabase.Contains("PVC"));
            sb.AppendLine();

            sb.AppendLine("[FittingLoss]");
            Check(sb, ref total, ref passed, "Codo 90 radio largo: K = 0.7",
                () => Approx(FittingLossDatabase.GetK("elbow90_long"), 0.7, 1e-9));
            Check(sb, ref total, ref passed, "Codo 90 a v=1 m/s, agua: dP = 350 Pa",
                () => Approx(FittingLoss.Of("elbow90_long").CalculateLossPa(1.0, 1000.0), 350.0, 1.0));
            Check(sb, ref total, ref passed, "Vavula retencion: K ~ 3.0",
                () => Approx(FittingLossDatabase.GetK("valve_check"), 3.0, 1e-9));
            Check(sb, ref total, ref passed, "Tipo desconocido -> K = 0",
                () => Approx(FittingLossDatabase.GetK("XXX"), 0.0, 1e-12));
            sb.AppendLine();

            sb.AppendLine("[NominalDiameters]");
            Check(sb, ref total, ref passed, "StandardSeries == SteelEN10255",
                () => ReferenceEquals(NominalDiameters.StandardSeries, NominalDiameters.SteelEN10255));
            Check(sb, ref total, ref passed, "Find(\"DN50\"): Di ~ 53.1 mm",
                () => { var dn = NominalDiameters.Find("DN50"); return dn != null && Approx(dn.InnerDiameterM, 0.0531, 1e-6); });
            Check(sb, ref total, ref passed, "SeriesForMaterial(\"COPPER\") == CopperEN1057",
                () => ReferenceEquals(NominalDiameters.SeriesForMaterial("COPPER"), NominalDiameters.CopperEN1057));
            Check(sb, ref total, ref passed, "NextUpForInner(0.040) -> DN40 (Di=41.9 mm)",
                () => { var dn = NominalDiameters.NextUpForInner(0.040); return dn != null && dn.Dn == 40; });
            Check(sb, ref total, ref passed, "NextUpForInner(0.045) -> DN50 (DN40 no llega)",
                () => { var dn = NominalDiameters.NextUpForInner(0.045); return dn != null && dn.Dn == 50; });
            sb.AppendLine();

            sb.AppendLine("[HydraulicCalculator]");
            var hc = new HydraulicCalculator();
            var seg = new PipeSegment
            {
                DiameterM = 0.0531, LengthM = 10.0, FlowM3s = 0.001,
                MaterialKey = "STEEL_GALV",
                Roughness = MaterialDatabase.GetRoughness("STEEL_GALV"),
            };
            var rSeg = hc.CalculateSegment(seg, w20);
            Check(sb, ref total, ref passed, "CalculateSegment: v ~ 0.45 m/s",
                () => Approx(rSeg.VelocityMs, 0.451, 0.01));
            Check(sb, ref total, ref passed, "CalculateSegment: Re > 20000",
                () => rSeg.ReynoldsNumber > 20000.0);
            Check(sb, ref total, ref passed, "CalculateSegment: f en [0.025, 0.040]",
                () => rSeg.FrictionFactor > 0.025 && rSeg.FrictionFactor < 0.040);
            Check(sb, ref total, ref passed, "CalculateSegment: dP/m > 0",
                () => rSeg.FrictionPaPm > 0.0);
            Check(sb, ref total, ref passed, "Dimensionado Q=2 L/s, v_max=2.0 -> DN32",
                () => { var dn = hc.DimensionSegment(0.002, SizingCriteria.MaxVelocity, 2.0, "STEEL_GALV", w20);
                        return dn != null && dn.Dn == 32; });
            Check(sb, ref total, ref passed, "Dimensionado Q=2 L/s, v_max=1.5 -> DN40",
                () => { var dn = hc.DimensionSegment(0.002, SizingCriteria.MaxVelocity, 1.5, "STEEL_GALV", w20);
                        return dn != null && dn.Dn == 40; });
            Check(sb, ref total, ref passed, "Dimensionado Q=10 L/s, v_max=2 -> DN >= 80",
                () => { var dn = hc.DimensionSegment(0.010, SizingCriteria.MaxVelocity, 2.0, "STEEL_GALV", w20);
                        return dn != null && dn.Dn >= 80; });
            Check(sb, ref total, ref passed, "Dimensionado por friccion 200 Pa/m, Q=2 L/s -> DN >= 40",
                () => { var dn = hc.DimensionSegment(0.002, SizingCriteria.MaxFriction, 200.0, "STEEL_GALV", w20);
                        return dn != null && dn.Dn >= 40; });
            var net = new PipeNetwork { SystemName = "Test", SourcePressurePa = 300000.0 };
            var pA = net.CreateNode(0.0, "A"); pA.IsSource = true;
            var pB = net.CreateNode(0.0, "B");
            var pC = net.CreateNode(0.0, "C");
            pC.IsTerminal = true; pC.DemandM3s = 0.001;
            net.AddNode(pA); net.AddNode(pB); net.AddNode(pC);
            net.SourceNode = pA; net.TerminalNodes.Add(pC);
            net.ConnectNodes(pA, pB, 0.0531, 5.0, "STEEL_GALV");
            net.ConnectNodes(pB, pC, 0.0531, 5.0, "STEEL_GALV");
            var rep = hc.SolveTreeNetwork(net, w20);
            Check(sb, ref total, ref passed, "Red A->B->C: 2 tramos calculados",
                () => rep.Segments.Count == 2);
            Check(sb, ref total, ref passed, "Red A->B->C: Q = 0.001 m3/s en ambos",
                () => net.Segments.All(s => Math.Abs(s.FlowM3s - 0.001) < 1e-9));
            Check(sb, ref total, ref passed, "Red A->B->C: P(C) < P(A)",
                () => pC.PressurePa < pA.PressurePa);
            Check(sb, ref total, ref passed, "Red A->B->C: WorstCasePressureDropPa > 0",
                () => rep.WorstCasePressureDropPa > 0.0);
            Check(sb, ref total, ref passed, "Red A->B->C: rep.Success == true",
                () => rep.Success);
            var netCycle = new PipeNetwork { SourcePressurePa = 100000 };
            var x1 = netCycle.CreateNode(0, "x1"); x1.IsSource = true; netCycle.SourceNode = x1;
            var x2 = netCycle.CreateNode(0, "x2");
            var x3 = netCycle.CreateNode(0, "x3");
            netCycle.ConnectNodes(x1, x2, 0.05, 5, "STEEL_GALV");
            netCycle.ConnectNodes(x2, x3, 0.05, 5, "STEEL_GALV");
            netCycle.ConnectNodes(x3, x1, 0.05, 5, "STEEL_GALV");
            var repCyc = hc.SolveTreeNetwork(netCycle, w20);
            Check(sb, ref total, ref passed, "Red con ciclo -> error CYCLE en informe",
                () => repCyc.Messages.Any(m => m.Code == "CYCLE"));
            sb.AppendLine();

            // =====================================================================
            //  Sprint 3 - Core.Aeraulics
            // =====================================================================

            sb.AppendLine("[AirProperties]");
            Check(sb, ref total, ref passed,
                "Aire 20 C nivel mar: rho ~ 1.204 kg/m3",
                () => Approx(AirProperties.Density(20.0, 0.0), 1.204, 0.005));
            Check(sb, ref total, ref passed,
                "Aire 20 C a 2000 m altitud: rho < 1.0 kg/m3",
                () => AirProperties.Density(20.0, 2000.0) < 1.0);
            Check(sb, ref total, ref passed,
                "PressureAtAltitude(0) ~ 101325 Pa",
                () => Approx(AirProperties.PressureAtAltitude(0.0), 101325.0, 1.0));
            Check(sb, ref total, ref passed,
                "PressureAtAltitude(1000 m) ~ 89875 Pa",
                () => Approx(AirProperties.PressureAtAltitude(1000.0), 89875.0, 100.0));
            Check(sb, ref total, ref passed,
                "Aire 20 C: mu Sutherland ~ 1.81e-5 Pa.s",
                () => Approx(AirProperties.DynamicViscosity(20.0), 1.813e-5, 5e-7));
            Check(sb, ref total, ref passed,
                "AsFluid(20, 0).FluidType == Air",
                () => AirProperties.AsFluid(20.0, 0.0).FluidType == FluidType.Air);
            sb.AppendLine();

            sb.AppendLine("[DuctSegment]");
            var d1 = new DuctSegment
            {
                Shape = DuctShape.Circular, DiameterM = 0.200, LengthM = 5.0, FlowM3s = 0.2,
            };
            Check(sb, ref total, ref passed, "Circular DN200: Area ~ 0.0314 m2",
                () => Approx(d1.AreaM2, 0.0314, 0.001));
            Check(sb, ref total, ref passed, "Circular DN200: D_h == D",
                () => Approx(d1.HydraulicDiameterM, 0.200, 1e-9));
            Check(sb, ref total, ref passed, "Circular DN200: SizeLabel empieza por 200",
                () => d1.SizeLabel.StartsWith("200"));

            var d2 = new DuctSegment
            {
                Shape = DuctShape.Rectangular, WidthM = 0.400, HeightM = 0.200,
                LengthM = 5.0, FlowM3s = 0.5,
            };
            Check(sb, ref total, ref passed, "Rect 400x200: Area = 0.08 m2",
                () => Approx(d2.AreaM2, 0.08, 1e-6));
            Check(sb, ref total, ref passed, "Rect 400x200: D_h = 2*W*H/(W+H) = 0.267",
                () => Approx(d2.HydraulicDiameterM, 0.2667, 1e-3));
            Check(sb, ref total, ref passed, "Rect 400x200: D_eq Huebscher ~ 0.305 (ASHRAE)",
                () => Approx(d2.EquivalentDiameterM, 0.3047, 5e-3));
            Check(sb, ref total, ref passed, "Rect 400x200: SizeLabel == \"400x200\"",
                () => d2.SizeLabel == "400x200");
            sb.AppendLine();

            sb.AppendLine("[DuctFittingLoss]");
            Check(sb, ref total, ref passed, "Codo circular R=1.5D: K = 0.20",
                () => Approx(DuctFittingLossDatabase.GetK("elbow_circ_r1.5d"), 0.20, 1e-9));
            Check(sb, ref total, ref passed, "Codo rect sin guias: K = 1.20",
                () => Approx(DuctFittingLossDatabase.GetK("elbow_rect_no_vanes"), 1.20, 1e-9));
            Check(sb, ref total, ref passed, "Codo R=1.5D a v=5 m/s, aire: dP ~ 3.0 Pa",
                () =>
                {
                    var fit = DuctFittingLoss.Of("elbow_circ_r1.5d");
                    return Approx(fit.CalculateLossPa(5.0, 1.204), 3.01, 0.05);
                });
            Check(sb, ref total, ref passed, "Atenuador acustico: K ~ 10",
                () => Approx(DuctFittingLossDatabase.GetK("silencer"), 10.0, 1e-9));
            Check(sb, ref total, ref passed, "Tipo desconocido -> K = 0",
                () => Approx(DuctFittingLossDatabase.GetK("XXX"), 0.0, 1e-12));
            sb.AppendLine();

            sb.AppendLine("[DuctSizingSeries]");
            Check(sb, ref total, ref passed, "Serie circular tiene >= 10 tallas",
                () => DuctSizingSeries.Circular.Count >= 10);
            Check(sb, ref total, ref passed, "FindCircular(200) -> D = 0.200",
                () => { var c = DuctSizingSeries.FindCircular(200);
                        return c != null && Approx(c.DiameterM, 0.200, 1e-9); });
            Check(sb, ref total, ref passed, "NextUpCircular(0.180) -> DN200",
                () => { var c = DuctSizingSeries.NextUpCircular(0.180);
                        return c != null && c.Dn == 200; });
            Check(sb, ref total, ref passed, "NextUpCircular(0.310) -> DN315",
                () => { var c = DuctSizingSeries.NextUpCircular(0.310);
                        return c != null && c.Dn == 315; });
            Check(sb, ref total, ref passed, "Rectangular 400x200 existe en serie",
                () => DuctSizingSeries.Rectangular.Any(r => r.Label == "400x200"));
            sb.AppendLine();

            sb.AppendLine("[AeraulicCalculator]");
            var ac = new AeraulicCalculator();
            var air20 = AirProperties.AsFluid(20.0, 0.0);
            var dseg = new DuctSegment
            {
                Shape = DuctShape.Circular, DiameterM = 0.200,
                LengthM = 10.0, FlowM3s = 0.2, Roughness = 0.0001,
            };
            var rDseg = ac.CalculateSegment(dseg, air20);
            Check(sb, ref total, ref passed,
                "DN200, Q=0.2 m3/s: v ~ 6.37 m/s",
                () => Approx(rDseg.VelocityMs, 6.37, 0.05));
            Check(sb, ref total, ref passed,
                "DN200 turbulento: Re > 50000",
                () => rDseg.ReynoldsNumber > 50000.0);
            Check(sb, ref total, ref passed,
                "DN200: dP/m en rango razonable [1, 5] Pa/m",
                () => rDseg.FrictionPaPm > 1.0 && rDseg.FrictionPaPm < 5.0);
            Check(sb, ref total, ref passed,
                "Dim circ Q=0.5, v_max=6 m/s -> DN >= 315",
                () =>
                {
                    var c = ac.DimensionCircularByVelocity(0.5, 6.0);
                    return c != null && c.Dn >= 315;
                });
            Check(sb, ref total, ref passed,
                "Dim circ Q=0.1, v_max=4 m/s -> DN >= 200",
                () =>
                {
                    var c = ac.DimensionCircularByVelocity(0.1, 4.0);
                    return c != null && c.Dn >= 200;
                });
            Check(sb, ref total, ref passed,
                "Dim circ por fricc 1 Pa/m, Q=0.5 -> DN >= 315",
                () =>
                {
                    var c = ac.DimensionCircularByEqualFriction(0.5, 1.0, air20);
                    return c != null && c.Dn >= 315;
                });
            Check(sb, ref total, ref passed,
                "Dim rect Q=0.5, v_max=6 m/s: AreaM2 >= 0.083",
                () =>
                {
                    var r = ac.DimensionRectangularByVelocity(0.5, 6.0);
                    return r != null && r.AreaM2 >= 0.083;
                });

            var dnet = new DuctNetwork { SystemName = "Supply", SourcePressurePa = 200.0 };
            var dA = dnet.CreateNode(0, "A"); dA.IsSource = true;
            var dB = dnet.CreateNode(0, "B");
            var dC = dnet.CreateNode(0, "C");
            dC.IsTerminal = true; dC.DemandM3s = 0.1;
            dnet.AddNode(dA); dnet.AddNode(dB); dnet.AddNode(dC);
            dnet.SourceNode = dA; dnet.TerminalNodes.Add(dC);
            dnet.ConnectCircular(dA, dB, 0.250, 5.0, "Supply");
            dnet.ConnectCircular(dB, dC, 0.200, 5.0, "Supply");
            var dRep = ac.SolveTreeNetwork(dnet, air20);
            Check(sb, ref total, ref passed,
                "Red ducts A->B->C: 2 tramos calculados",
                () => dRep.Segments.Count == 2);
            Check(sb, ref total, ref passed,
                "Red ducts A->B->C: ambos tramos con Q = 0.1 m3/s",
                () => dnet.Segments.All(s => Math.Abs(s.FlowM3s - 0.1) < 1e-9));
            Check(sb, ref total, ref passed,
                "Red ducts A->B->C: P_t(C) < P_t(A)",
                () => dC.TotalPressurePa < dA.TotalPressurePa);
            Check(sb, ref total, ref passed,
                "Red ducts A->B->C: dP en tramo final > 0",
                () => dnet.Segments.Last().TotalLossPa > 0.0);
            Check(sb, ref total, ref passed,
                "Red ducts A->B->C: rep.Success == true",
                () => dRep.Success);
            sb.AppendLine();

            // =====================================================================
            //  Sprint 4 - RevitBridge (constantes y conversiones)
            // =====================================================================

            sb.AppendLine("[RevitBridge - constantes]");
            Check(sb, ref total, ref passed, "ResultWriter.ParamVelocity == MEP_CALC_VELOCITY",
                () => ResultWriter.ParamVelocity == "MEP_CALC_VELOCITY");
            Check(sb, ref total, ref passed, "ResultWriter.ParamFrictionPaPm == MEP_CALC_FRICTION_PA_M",
                () => ResultWriter.ParamFrictionPaPm == "MEP_CALC_FRICTION_PA_M");
            Check(sb, ref total, ref passed, "ResultWriter.ParamTotalLossPa == MEP_CALC_TOTAL_LOSS_PA",
                () => ResultWriter.ParamTotalLossPa == "MEP_CALC_TOTAL_LOSS_PA");
            Check(sb, ref total, ref passed, "ResultWriter.ParamReynolds == MEP_CALC_REYNOLDS",
                () => ResultWriter.ParamReynolds == "MEP_CALC_REYNOLDS");
            Check(sb, ref total, ref passed, "ResultWriter.ParamOk == MEP_CALC_OK",
                () => ResultWriter.ParamOk == "MEP_CALC_OK");
            Check(sb, ref total, ref passed, "ResultWriter.ParamRecommendedDn == MEP_CALC_RECOMMENDED_DN",
                () => ResultWriter.ParamRecommendedDn == "MEP_CALC_RECOMMENDED_DN");
            sb.AppendLine();

            sb.AppendLine("[RevitBridge - RevitUnits round-trips]");
            Check(sb, ref total, ref passed,
                "Length round-trip: 1 m SI -> ft Revit -> 1 m",
                () => Approx(RevitUnits.LengthToSI(RevitUnits.LengthFromSI(1.0)), 1.0, 1e-9));
            Check(sb, ref total, ref passed,
                "Length 1 m SI -> 3.2808 ft Revit",
                () => Approx(RevitUnits.LengthFromSI(1.0), 3.28084, 1e-3));
            Check(sb, ref total, ref passed,
                "Flow round-trip: 0.001 m3/s SI -> Revit -> 0.001 m3/s",
                () => Approx(RevitUnits.FlowToSI(RevitUnits.FlowFromSI(0.001)), 0.001, 1e-12));
            Check(sb, ref total, ref passed,
                "Pressure round-trip: 100 Pa SI -> Revit -> 100 Pa",
                () => Approx(RevitUnits.PressureToSI(RevitUnits.PressureFromSI(100.0)), 100.0, 1e-9));
            Check(sb, ref total, ref passed,
                "Friction round-trip: 200 Pa/m SI -> Revit -> 200 Pa/m",
                () => Approx(RevitUnits.FrictionToSI(RevitUnits.FrictionFromSI(200.0)), 200.0, 1e-9));
            Check(sb, ref total, ref passed,
                "Velocity round-trip: 2.5 m/s SI -> Revit -> 2.5 m/s",
                () => Approx(RevitUnits.VelocityToSI(RevitUnits.VelocityFromSI(2.5)), 2.5, 1e-9));
            sb.AppendLine();

            // =====================================================================
            //  Sprint 5 - UI WPF (logica testable sin instanciar ventana)
            // =====================================================================

            sb.AppendLine("[Sprint 5 - UI ResultRow / PresetItem]");
            Check(sb, ref total, ref passed,
                "ResultRow: CalcResult limpio -> StatusKind=OK",
                () =>
                {
                    var rcr = new CalcResult { RevitElementId = 1L };
                    var row = new CalcWindow.ResultRow();
                    row.SetStatusFrom(rcr);
                    return row.StatusKind == "OK" && row.Status == "OK";
                });
            Check(sb, ref total, ref passed,
                "ResultRow: VelocityExceeded -> StatusKind=Warning",
                () =>
                {
                    var rcr = new CalcResult { RevitElementId = 1L, VelocityExceeded = true };
                    var row = new CalcWindow.ResultRow();
                    row.SetStatusFrom(rcr);
                    return row.StatusKind == "Warning" && row.Status.Contains("v excede");
                });
            Check(sb, ref total, ref passed,
                "ResultRow: FrictionExceeded -> StatusKind=Warning",
                () =>
                {
                    var rcr = new CalcResult { RevitElementId = 1L, FrictionExceeded = true };
                    var row = new CalcWindow.ResultRow();
                    row.SetStatusFrom(rcr);
                    return row.StatusKind == "Warning";
                });
            Check(sb, ref total, ref passed,
                "ResultRow: Warning de Core -> StatusKind=Warning",
                () =>
                {
                    var rcr = new CalcResult { RevitElementId = 1L };
                    rcr.Warnings.Add(CalcWarning.Warn("W1", "ojo"));
                    var row = new CalcWindow.ResultRow();
                    row.SetStatusFrom(rcr);
                    return row.StatusKind == "Warning";
                });
            Check(sb, ref total, ref passed,
                "ResultRow: Error de Core -> StatusKind=Error",
                () =>
                {
                    var rcr = new CalcResult { RevitElementId = 1L };
                    rcr.Warnings.Add(CalcWarning.Error("E1", "fallo"));
                    var row = new CalcWindow.ResultRow();
                    row.SetStatusFrom(rcr);
                    return row.StatusKind == "Error" && row.Status.Contains("fallo");
                });
            Check(sb, ref total, ref passed,
                "ResultRow: result null -> Sin calcular (Warning)",
                () =>
                {
                    var row = new CalcWindow.ResultRow();
                    row.SetStatusFrom(null);
                    return row.StatusKind == "Warning" && row.Status == "Sin calcular";
                });
            Check(sb, ref total, ref passed,
                "PresetItem.ToString devuelve Label",
                () =>
                {
                    var p = new CalcWindow.PresetItem("CTE_AF", "Mi etiqueta");
                    return p.ToString() == "Mi etiqueta" && p.Key == "CTE_AF";
                });
            sb.AppendLine();

            // =====================================================================
            //  Sprint 6 - DuctNetwork end-to-end (preset dinamico, OrientFromSource,
            //  RunFullCalculation, integracion AeraulicCalculator + ResultWriter
            //  constantes).  No abre Revit; testea logica pura sobre red sintetica.
            // =====================================================================

            sb.AppendLine("[Sprint 6 - Presets aeraulicos]");
            Check(sb, ref total, ref passed,
                "Preset RITE_SUPPLY: SupplyAir, MaxFriction = 1 Pa/m",
                () =>
                {
                    var p = SizingCriteriaConfig.FromPreset("RITE_SUPPLY");
                    return p.Service == ServiceType.SupplyAir
                        && Approx(p.MaxFrictionPaPm, 1.0, 1e-9)
                        && p.Fluid == FluidType.Air;
                });
            Check(sb, ref total, ref passed,
                "Preset RITE_RETURN: ReturnAir, v = 6 m/s",
                () =>
                {
                    var p = SizingCriteriaConfig.FromPreset("RITE_RETURN");
                    return p.Service == ServiceType.ReturnAir
                        && Approx(p.MaxVelocityMs, 6.0, 1e-9);
                });
            Check(sb, ref total, ref passed,
                "Preset RITE_EXHAUST: ExhaustAir, v = 5 m/s, criterio MaxVelocity",
                () =>
                {
                    var p = SizingCriteriaConfig.FromPreset("RITE_EXHAUST");
                    return p.Service == ServiceType.ExhaustAir
                        && Approx(p.MaxVelocityMs, 5.0, 1e-9)
                        && p.Criterion == SizingCriteria.MaxVelocity;
                });
            Check(sb, ref total, ref passed,
                "Presets dict contiene RITE_EXHAUST",
                () => SizingCriteriaConfig.Presets.ContainsKey("RITE_EXHAUST"));
            sb.AppendLine();

            sb.AppendLine("[Sprint 6 - DuctNetwork.OrientFromSource]");
            // Red 3 nudos: A (source) - B - C (terminal). Construye con NodeStart/End
            // invertidos a proposito; OrientFromSource debe reordenarlos.
            var orNet = new DuctNetwork { SystemName = "Orient", SourcePressurePa = 200.0 };
            var oA = orNet.CreateNode(0, "A"); oA.IsSource = true;
            var oB = orNet.CreateNode(0, "B");
            var oC = orNet.CreateNode(0, "C"); oC.IsTerminal = true; oC.DemandM3s = 0.05;
            orNet.SourceNode = oA;
            orNet.TerminalNodes.Add(oC);
            // Tramos voluntariamente al reves (NodeStart=B, NodeEnd=A) y (C, B)
            var orSeg1 = new DuctSegment { Shape = DuctShape.Circular, DiameterM = 0.200,
                LengthM = 5.0, NodeStart = oB, NodeEnd = oA };
            orNet.AddSegment(orSeg1);
            var orSeg2 = new DuctSegment { Shape = DuctShape.Circular, DiameterM = 0.160,
                LengthM = 5.0, NodeStart = oC, NodeEnd = oB };
            orNet.AddSegment(orSeg2);
            orNet.OrientFromSource();
            Check(sb, ref total, ref passed,
                "OrientFromSource: tramo A->B queda con Start=A",
                () => ReferenceEquals(orSeg1.NodeStart, oA) && ReferenceEquals(orSeg1.NodeEnd, oB));
            Check(sb, ref total, ref passed,
                "OrientFromSource: tramo B->C queda con Start=B",
                () => ReferenceEquals(orSeg2.NodeStart, oB) && ReferenceEquals(orSeg2.NodeEnd, oC));
            sb.AppendLine();

            sb.AppendLine("[Sprint 6 - End-to-end: red UTA -> 3 terminales]");
            // UTA (source) en cubierta, 1 troncal, 3 ramales con difusores.
            var e2eNet = new DuctNetwork { SystemName = "Supply Air", SourcePressurePa = 250.0 };
            var nU  = e2eNet.CreateNode(0, "UTA");  nU.IsSource = true;
            var nT  = e2eNet.CreateNode(0, "Troncal");
            var nR1 = e2eNet.CreateNode(0, "Difusor-1"); nR1.IsTerminal = true; nR1.DemandM3s = 0.05;
            var nR2 = e2eNet.CreateNode(0, "Difusor-2"); nR2.IsTerminal = true; nR2.DemandM3s = 0.05;
            var nR3 = e2eNet.CreateNode(0, "Difusor-3"); nR3.IsTerminal = true; nR3.DemandM3s = 0.05;
            e2eNet.SourceNode = nU;
            e2eNet.TerminalNodes.Add(nR1);
            e2eNet.TerminalNodes.Add(nR2);
            e2eNet.TerminalNodes.Add(nR3);
            e2eNet.ConnectCircular(nU, nT, 0.250, 8.0, "Supply Air");
            e2eNet.ConnectCircular(nT, nR1, 0.160, 4.0, "Supply Air");
            e2eNet.ConnectCircular(nT, nR2, 0.160, 4.0, "Supply Air");
            e2eNet.ConnectCircular(nT, nR3, 0.160, 4.0, "Supply Air");
            e2eNet.OrientFromSource();

            var ac6 = new AeraulicCalculator();
            // Renombrado a air20e2e: ya existe `air20` en el ambito (sprint 4).
            var air20e2e = AirProperties.AsFluid(20.0, 0.0);
            var crit6 = SizingCriteriaConfig.FromPreset("RITE_SUPPLY");
            var rep6 = ac6.RunFullCalculation(e2eNet, air20e2e, crit6);

            Check(sb, ref total, ref passed,
                "End-to-end: 4 tramos calculados",
                () => rep6.Segments.Count == 4);
            Check(sb, ref total, ref passed,
                "End-to-end: caudal en troncal = suma terminales (0.15 m3/s)",
                () => Approx(e2eNet.Segments[0].FlowM3s, 0.15, 1e-9));
            Check(sb, ref total, ref passed,
                "End-to-end: caudal en cada ramal = 0.05 m3/s",
                () => e2eNet.Segments.Skip(1).All(s => Approx(s.FlowM3s, 0.05, 1e-9)));
            Check(sb, ref total, ref passed,
                "End-to-end: WorstCasePressureDropPa > 0",
                () => rep6.WorstCasePressureDropPa > 0.0);
            Check(sb, ref total, ref passed,
                "End-to-end: P_t en cada terminal < 250 Pa (fuente)",
                () => new[] { nR1, nR2, nR3 }.All(n => n.TotalPressurePa < 250.0));
            Check(sb, ref total, ref passed,
                "End-to-end: ningun tramo en error",
                () => rep6.CountErrors == 0);
            Check(sb, ref total, ref passed,
                "End-to-end: presion troncal entre fuente y terminales",
                () => nT.TotalPressurePa < nU.TotalPressurePa
                   && nT.TotalPressurePa > nR1.TotalPressurePa);
            sb.AppendLine();

            sb.AppendLine("[Sprint 6 - End-to-end: criterio dinamico Exhaust]");
            // Mismo grafo evaluado con preset EXHAUST: caudales identicos pero
            // limite de velocidad mas estricto.  Algun ramal puede entrar en
            // VelocityExceeded (es esperable y el report debe marcarlo, no fallar).
            var exhNet = new DuctNetwork { SystemName = "Exhaust WC", SourcePressurePa = 200.0 };
            var eU = exhNet.CreateNode(0, "Vent"); eU.IsSource = true; exhNet.SourceNode = eU;
            var eR = exhNet.CreateNode(0, "Rejilla"); eR.IsTerminal = true; eR.DemandM3s = 0.20;
            exhNet.TerminalNodes.Add(eR);
            // Conducto deliberadamente pequeño -> velocidad alta para forzar exceso
            exhNet.ConnectCircular(eU, eR, 0.160, 5.0, "Exhaust");
            exhNet.OrientFromSource();
            var critExh = SizingCriteriaConfig.FromPreset("RITE_EXHAUST");
            var repExh = ac6.RunFullCalculation(exhNet, air20e2e, critExh);
            Check(sb, ref total, ref passed,
                "Exhaust: tramo calculado",
                () => repExh.Segments.Count == 1);
            Check(sb, ref total, ref passed,
                "Exhaust: velocidad calculada > 5 m/s en DN160 a 200 L/s -> VelocityExceeded=true",
                () =>
                {
                    var r = repExh.Segments.Values.First();
                    return r.VelocityMs > 5.0 && r.VelocityExceeded;
                });
            Check(sb, ref total, ref passed,
                "Exhaust: report.Success == false (criterio excedido)",
                () => !repExh.Success);
            sb.AppendLine();

            // =====================================================================
            //  Sprint 8 - Validación contra hand-calc (Tabla 11 del briefing)
            //
            //  Notas:
            //    - Las cifras "ΔP/m esperado" del briefing son orientativas y no
            //      siempre consistentes con un cálculo Darcy-Colebrook estricto
            //      (algunas mezclan Q y D de fuentes distintas).
            //    - Aquí validamos que el motor produzca resultados FÍSICAMENTE
            //      coherentes: v exacta, Re en rango, f en banda esperada para
            //      el régimen, y ΔP/m igual a f·ρ·v²/(2D) (cierre interno).
            //    - El caso laminar comprueba que f = 64/Re exacto y que ΔP/m
            //      coincide con Hagen-Poiseuille (32·μ·v/D²).
            // =====================================================================

            sb.AppendLine("[Sprint 8 - Hand-calc validation (Tabla 11)]");

            // ---- Caso 1: acero galv DN50, agua 20°C, Q = 0.55 L/s ---------
            var hc8 = new HydraulicCalculator();
            var w20v = FluidProperties.Water20C;
            var seg1 = new PipeSegment
            {
                DiameterM   = 0.0503,
                LengthM     = 10.0,
                FlowM3s     = 0.00055,
                MaterialKey = "STEEL_GALV",
                Roughness   = MaterialDatabase.GetRoughness("STEEL_GALV"),
            };
            var r1 = hc8.CalculateSegment(seg1, w20v);
            Check(sb, ref total, ref passed,
                "Caso 1 (DN50 galv, 0.55 L/s): v ~ 0.277 m/s",
                () => Approx(r1.VelocityMs, 0.277, 0.005));
            Check(sb, ref total, ref passed,
                "Caso 1: Re ~ 13900 (turbulento)",
                () => Approx(r1.ReynoldsNumber, 13900.0, 400.0));
            Check(sb, ref total, ref passed,
                "Caso 1: f en [0.028, 0.040] (acero galv parcialmente rugoso)",
                () => r1.FrictionFactor > 0.028 && r1.FrictionFactor < 0.040);
            Check(sb, ref total, ref passed,
                "Caso 1: dP/m coherente con f·ρ·v²/(2D)",
                () =>
                {
                    double expected = r1.FrictionFactor * w20v.Density
                                      * r1.VelocityMs * r1.VelocityMs
                                      / (2.0 * 0.0503);
                    return Approx(r1.FrictionPaPm, expected, 0.5);
                });

            // ---- Caso 2: cobre DN25, agua 20°C, Q = 0.20 L/s --------------
            var seg2 = new PipeSegment
            {
                DiameterM   = 0.0268,
                LengthM     = 10.0,
                FlowM3s     = 0.00020,
                MaterialKey = "COPPER",
                Roughness   = MaterialDatabase.GetRoughness("COPPER"),
            };
            var r2 = hc8.CalculateSegment(seg2, w20v);
            Check(sb, ref total, ref passed,
                "Caso 2 (cobre DN25, 0.20 L/s): v ~ 0.355 m/s",
                () => Approx(r2.VelocityMs, 0.355, 0.01));
            Check(sb, ref total, ref passed,
                "Caso 2: Re ~ 9500 (turbulento)",
                () => Approx(r2.ReynoldsNumber, 9500.0, 400.0));
            Check(sb, ref total, ref passed,
                "Caso 2: f en [0.028, 0.035] (cobre liso)",
                () => r2.FrictionFactor > 0.028 && r2.FrictionFactor < 0.035);
            Check(sb, ref total, ref passed,
                "Caso 2: dP/m coherente con Darcy",
                () =>
                {
                    double expected = r2.FrictionFactor * w20v.Density
                                      * r2.VelocityMs * r2.VelocityMs
                                      / (2.0 * 0.0268);
                    return Approx(r2.FrictionPaPm, expected, 0.5);
                });

            // ---- Caso 3: aire 20°C, conducto chapa 400x200, Q = 0.5 m³/s --
            var ac8 = new AeraulicCalculator();
            var air20v = AirProperties.AsFluid(20.0, 0.0);
            var dseg3 = new DuctSegment
            {
                Shape     = DuctShape.Rectangular,
                WidthM    = 0.400, HeightM = 0.200,
                LengthM   = 10.0,  FlowM3s = 0.5,
                Roughness = 0.0001, // chapa galvanizada
            };
            var r3 = ac8.CalculateSegment(dseg3, air20v);
            Check(sb, ref total, ref passed,
                "Caso 3 (400x200): D_h ~ 0.267 m",
                () => Approx(dseg3.HydraulicDiameterM, 0.2667, 0.001));
            Check(sb, ref total, ref passed,
                "Caso 3: v = Q/A = 6.25 m/s",
                () => Approx(r3.VelocityMs, 6.25, 0.01));
            Check(sb, ref total, ref passed,
                "Caso 3: Re > 100 000 (turbulento desarrollado)",
                () => r3.ReynoldsNumber > 100000.0);
            Check(sb, ref total, ref passed,
                "Caso 3: f en [0.015, 0.022] (chapa galv turbulento)",
                () => r3.FrictionFactor > 0.015 && r3.FrictionFactor < 0.022);
            Check(sb, ref total, ref passed,
                "Caso 3: dP/m en rango razonable [1.0, 2.5] Pa/m",
                () => r3.FrictionPaPm > 1.0 && r3.FrictionPaPm < 2.5);

            // ---- Caso 4: flujo laminar agua, DN20, Q muy bajo -------------
            // Q escogida para forzar Re < 2300. Verifica que el motor cae al
            // ramal laminar f = 64/Re y que dP/m casa con Hagen-Poiseuille.
            var seg4 = new PipeSegment
            {
                DiameterM   = 0.020,
                LengthM     = 10.0,
                FlowM3s     = 1.0e-5,   // 0.01 L/s → laminar garantizado
                MaterialKey = "STEEL_GALV",
                Roughness   = MaterialDatabase.GetRoughness("STEEL_GALV"),
            };
            var r4 = hc8.CalculateSegment(seg4, w20v);
            Check(sb, ref total, ref passed,
                "Caso 4 (DN20, Q=0.01 L/s): Re < 2300 (laminar)",
                () => r4.ReynoldsNumber < 2300.0);
            Check(sb, ref total, ref passed,
                "Caso 4: f == 64/Re exacto (rama laminar)",
                () => Approx(r4.FrictionFactor, 64.0 / r4.ReynoldsNumber, 1e-6));
            Check(sb, ref total, ref passed,
                "Caso 4: dP/m == Hagen-Poiseuille 32·μ·v/D²",
                () =>
                {
                    double hp = 32.0 * w20v.DynamicViscosity * r4.VelocityMs
                                / (0.020 * 0.020);
                    return Approx(r4.FrictionPaPm, hp, 0.5);
                });

            // ---- Closure global: Darcy vs Colebrook -----------------------
            // Sanity check para garantizar que el motor cierra Darcy sobre
            // un rango amplio de Re y rugosidades — pruebas aleatorias
            // controladas para detectar regresiones futuras.
            int closureChecks = 0, closureFails = 0;
            int[] reSamples = { 5000, 25000, 100000, 500000 };
            double[] roughnessSamples = { 0.000015, 0.000046, 0.000150, 0.000260 };
            foreach (int reS in reSamples)
            {
                foreach (double rgh in roughnessSamples)
                {
                    closureChecks++;
                    double D = 0.05;
                    double v = reS * w20v.DynamicViscosity / (w20v.Density * D);
                    double Q = v * Math.PI * D * D / 4.0;
                    var sx = new PipeSegment { DiameterM = D, LengthM = 1.0,
                        FlowM3s = Q, MaterialKey = "X", Roughness = rgh };
                    var rx = hc8.CalculateSegment(sx, w20v);
                    double expectedDp = rx.FrictionFactor * w20v.Density
                                        * rx.VelocityMs * rx.VelocityMs
                                        / (2.0 * D);
                    if (!Approx(rx.FrictionPaPm, expectedDp, 0.1)) closureFails++;
                }
            }
            Check(sb, ref total, ref passed,
                $"Cierre Darcy (16 combos Re×ε): 0 fallos (real={closureFails}/{closureChecks})",
                () => closureFails == 0);

            sb.AppendLine();

            // =====================================================================
            //  Sprint 8 punto 2 - K de tés dependiente del cociente de caudal β
            // =====================================================================

            sb.AppendLine("[Sprint 8 - Tee K(β) dependiente de caudal]");

            // ---- Tuberías -------------------------------------------------
            Check(sb, ref total, ref passed,
                "Pipe T paso directo β=0: K = 0.3",
                () => Approx(FittingLossDatabase.GetTeeThroughK(0.0), 0.30, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe T paso directo β=1: K = 0.6 (extremo briefing)",
                () => Approx(FittingLossDatabase.GetTeeThroughK(1.0), 0.60, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe T paso directo β=0.5: K = 0.375 (cuadrática)",
                () => Approx(FittingLossDatabase.GetTeeThroughK(0.5), 0.375, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe T derivación β=0: K = 0.8",
                () => Approx(FittingLossDatabase.GetTeeBranchK(0.0), 0.80, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe T derivación β=1: K = 1.8 (extremo briefing)",
                () => Approx(FittingLossDatabase.GetTeeBranchK(1.0), 1.80, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe T derivación β=0.5: K = 1.3 (lineal)",
                () => Approx(FittingLossDatabase.GetTeeBranchK(0.5), 1.30, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe T β fuera de rango (β=2.5): clampeado a 1 → K=1.8",
                () => Approx(FittingLossDatabase.GetTeeBranchK(2.5), 1.80, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe T β negativo (β=-0.3): clampeado a 0 → K=0.3 (through)",
                () => Approx(FittingLossDatabase.GetTeeThroughK(-0.3), 0.30, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe FittingLoss.OfTeeThrough(0.5).K = 0.375",
                () => Approx(FittingLoss.OfTeeThrough(0.5).KCoefficient, 0.375, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe FittingLoss.OfTeeBranch(0.7).K = 1.5",
                () => Approx(FittingLoss.OfTeeBranch(0.7).KCoefficient, 1.50, 1e-9));
            Check(sb, ref total, ref passed,
                "Pipe FittingLoss.OfTeeBranch lleva etiqueta con β",
                () => FittingLoss.OfTeeBranch(0.4).Label.Contains("β"));
            Check(sb, ref total, ref passed,
                "Pipe FittingLoss legacy Of(\"tee_through\") sigue funcionando",
                () => Approx(FittingLoss.Of("tee_through").KCoefficient, 0.5, 1e-9));

            // ---- Conductos ------------------------------------------------
            Check(sb, ref total, ref passed,
                "Duct T paso directo β=0: K = 0.10",
                () => Approx(DuctFittingLossDatabase.GetTeeThroughK(0.0), 0.10, 1e-9));
            Check(sb, ref total, ref passed,
                "Duct T paso directo β=1: K = 0.30 (anchor briefing)",
                () => Approx(DuctFittingLossDatabase.GetTeeThroughK(1.0), 0.30, 1e-9));
            Check(sb, ref total, ref passed,
                "Duct T derivación β=0: K = 0.50",
                () => Approx(DuctFittingLossDatabase.GetTeeBranchK(0.0), 0.50, 1e-9));
            Check(sb, ref total, ref passed,
                "Duct T derivación β=1: K = 1.20 (anchor briefing)",
                () => Approx(DuctFittingLossDatabase.GetTeeBranchK(1.0), 1.20, 1e-9));
            Check(sb, ref total, ref passed,
                "Duct T derivación β=0.5: K = 0.85 (igual al valor 'tee_branch' legacy)",
                () => Approx(DuctFittingLossDatabase.GetTeeBranchK(0.5), 0.85, 1e-9));
            Check(sb, ref total, ref passed,
                "Duct DuctFittingLoss.OfTeeThrough(0.5).K = 0.15",
                () => Approx(DuctFittingLoss.OfTeeThrough(0.5).KCoefficient, 0.15, 1e-9));
            Check(sb, ref total, ref passed,
                "Duct DuctFittingLoss.OfTeeBranch(0.0).K = 0.50 con etiqueta",
                () =>
                {
                    var f = DuctFittingLoss.OfTeeBranch(0.0);
                    return Approx(f.KCoefficient, 0.50, 1e-9) && f.Label.Contains("β");
                });

            // ---- Monotonicidad (K crece con β) ----------------------------
            Check(sb, ref total, ref passed,
                "Pipe K_through monotónica creciente en β ∈ [0,1]",
                () =>
                {
                    double prev = -1;
                    for (double b = 0.0; b <= 1.0; b += 0.1)
                    {
                        double k = FittingLossDatabase.GetTeeThroughK(b);
                        if (k < prev - 1e-9) return false;
                        prev = k;
                    }
                    return true;
                });
            Check(sb, ref total, ref passed,
                "Pipe K_branch monotónica creciente en β ∈ [0,1]",
                () =>
                {
                    double prev = -1;
                    for (double b = 0.0; b <= 1.0; b += 0.1)
                    {
                        double k = FittingLossDatabase.GetTeeBranchK(b);
                        if (k < prev - 1e-9) return false;
                        prev = k;
                    }
                    return true;
                });
            Check(sb, ref total, ref passed,
                "Pipe K_branch > K_through ∀ β (rama siempre más pérdida que paso)",
                () =>
                {
                    for (double b = 0.0; b <= 1.0; b += 0.1)
                    {
                        double kb = FittingLossDatabase.GetTeeBranchK(b);
                        double kt = FittingLossDatabase.GetTeeThroughK(b);
                        if (kb <= kt) return false;
                    }
                    return true;
                });

            sb.AppendLine();

            // ---- Resumen ----
            sb.AppendLine("================================================================");
            sb.AppendLine("Resultado: " + passed + "/" + total + " OK   .   " + (total - passed) + " fallos");

            return new Report
            {
                Total    = total,
                Passed   = passed,
                Failed   = total - passed,
                FullText = sb.ToString(),
            };
        }

        // -- Internals -------------------------------------------------------

        private static void Check(StringBuilder sb, ref int total, ref int passed,
                                  string name, Func<bool> assertion)
        {
            total++;
            bool ok;
            string detail = "";
            try { ok = assertion(); }
            catch (Exception ex)
            {
                ok = false;
                detail = "  (" + ex.GetType().Name + ": " + ex.Message + ")";
            }
            if (ok) passed++;
            sb.AppendLine("  " + (ok ? "[OK] " : "[FAIL] ") + name + detail);
        }

        private static bool Approx(double actual, double expected, double tol)
            => !double.IsNaN(actual) && Math.Abs(actual - expected) <= tol;

        private sealed class Nd
        {
            public string Name { get; }
            public Nd(string name) { Name = name; }
            public override string ToString() => Name;
        }

        private sealed class Eg
        {
            public Nd S { get; }
            public Nd E { get; }
            public Eg(Nd s, Nd e) { S = s; E = e; }
        }
    }
}
