// ============================================================================
//  CalcSelfTest.cs
//  Tests — Smoke-tests del motor de calculo MEP.  Ejecutables sin NUnit.
//  Lanzado desde Revit por el comando CalcSelfTestCommand (boton ribbon).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;
using e3gbimtools.Calc.Core.Aeraulics;

namespace e3gbimtools.Calc.Tests
{
    public static class CalcSelfTest
    {
        public class Report
        {
            public int    Total     { get; set; }
            public int    Passed    { get; set; }
            public int    Failed    { get; set; }
            public string FullText  { get; set; }
            public bool   AllPassed => Failed == 0;
        }

        public static Report Run()
        {
            var sb     = new StringBuilder();
            int total  = 0;
            int passed = 0;

            sb.AppendLine("e3gbimtools.Calc — Self-Test MEP");
            sb.AppendLine("================================");
            sb.AppendLine();

            // ================================================================
            //  Core.Common — Units
            // ================================================================
            sb.AppendLine("[Units]");
            Check(sb, ref total, ref passed,
                "ft -> m round-trip",
                () => Approx(Units.FtToM(Units.MToFt(1.0)), 1.0));
            Check(sb, ref total, ref passed,
                "1 ft ≈ 0.3048 m",
                () => Approx(Units.FtToM(1.0), 0.3048));
            Check(sb, ref total, ref passed,
                "LpS_to_M3s",
                () => Approx(Units.LpsToM3s(1.0), 0.001));
            sb.AppendLine();

            // ================================================================
            //  Core.Common — FluidProperties
            // ================================================================
            sb.AppendLine("[FluidProperties]");
            Check(sb, ref total, ref passed,
                "Water20C density ≈ 998 kg/m3",
                () => Approx(FluidProperties.Water20C.Density, 998.0, tol: 5.0));
            Check(sb, ref total, ref passed,
                "Water60C density ≈ 983 kg/m3",
                () => Approx(FluidProperties.Water60C.Density, 983.0, tol: 5.0));
            Check(sb, ref total, ref passed,
                "AirProperties.Standard viscosity > 0",
                () => AirProperties.Standard.DynamicViscosity > 0.0);
            sb.AppendLine();

            // ================================================================
            //  Core.Common — SizingCriteriaConfig presets
            // ================================================================
            sb.AppendLine("[SizingCriteriaConfig]");
            Check(sb, ref total, ref passed,
                "Preset CTE_AF exists",
                () => SizingCriteriaConfig.Presets.ContainsKey("CTE_AF"));
            Check(sb, ref total, ref passed,
                "Preset RITE_SUPPLY exists",
                () => SizingCriteriaConfig.Presets.ContainsKey("RITE_SUPPLY"));
            Check(sb, ref total, ref passed,
                "FromPreset round-trip",
                () => SizingCriteriaConfig.FromPreset("RITE_HEATING") != null);
            Check(sb, ref total, ref passed,
                "Unknown preset throws",
                () => { try { SizingCriteriaConfig.FromPreset("NOPE"); return false; }
                        catch (KeyNotFoundException) { return true; } });
            sb.AppendLine();

            // ================================================================
            //  Core.Hydraulics — FrictionFactor
            // ================================================================
            sb.AppendLine("[FrictionFactor]");
            Check(sb, ref total, ref passed,
                "Hagen-Poiseuille f = 64/Re (Re=1000)",
                () => { double f = FrictionFactor.Calculate(1000.0, 0.0, 0.05);
                        return Approx(f, 64.0 / 1000.0, tol: 0.0005); });
            Check(sb, ref total, ref passed,
                "Turbulent f > 0 (Re=100000)",
                () => FrictionFactor.Calculate(100000.0, 0.000045, 0.1) > 0.0);
            sb.AppendLine();

            // ================================================================
            //  Core.Hydraulics — HydraulicCalculator (red sintetica 2 tramos)
            // ================================================================
            sb.AppendLine("[HydraulicCalculator]");
            try
            {
                var net = BuildSimplePipeNetwork();
                var fluid = FluidProperties.Water20C;
                var criteria = SizingCriteriaConfig.FromPreset("CTE_AF");
                var calc = new HydraulicCalculator();
                var report = calc.RunFullCalculation(net, fluid, criteria);

                Check(sb, ref total, ref passed,
                    "RunFullCalculation returns report",
                    () => report != null);
                Check(sb, ref total, ref passed,
                    "Segments == 2",
                    () => report.Segments.Count == 2);
                Check(sb, ref total, ref passed,
                    "Velocity > 0 on seg1",
                    () => report.Segments.Values.First().VelocityMs > 0.0);
                Check(sb, ref total, ref passed,
                    "TotalLossPa > 0 on seg1",
                    () => report.Segments.Values.First().TotalLossPa > 0.0);
            }
            catch (Exception ex)
            {
                sb.AppendLine("  [FAIL] HydraulicCalculator exception: " + ex.Message);
                total++;
            }
            sb.AppendLine();

            // ================================================================
            //  Core.Aeraulics — AeraulicCalculator (red sintetica 1 tramo)
            // ================================================================
            sb.AppendLine("[AeraulicCalculator]");
            try
            {
                var net = BuildSimpleDuctNetwork();
                var fluid = AirProperties.AsFluid(20.0, 0.0);
                var criteria = SizingCriteriaConfig.FromPreset("RITE_SUPPLY");
                var calc = new AeraulicCalculator();
                var report = calc.RunFullCalculation(net, fluid, criteria);

                Check(sb, ref total, ref passed,
                    "AeraulicCalculator returns report",
                    () => report != null);
                Check(sb, ref total, ref passed,
                    "Duct segment velocity > 0",
                    () => report.Segments.Count > 0 && report.Segments.Values.First().VelocityMs > 0.0);
            }
            catch (Exception ex)
            {
                sb.AppendLine("  [FAIL] AeraulicCalculator exception: " + ex.Message);
                total++;
            }
            sb.AppendLine();

            // ================================================================
            //  Core.Hydraulics — NominalDiameters
            // ================================================================
            sb.AppendLine("[NominalDiameters]");
            Check(sb, ref total, ref passed,
                "NextUpForInner 0.019 m devuelve resultado",
                () => NominalDiameters.NextUpForInner(0.019) != null);
            Check(sb, ref total, ref passed,
                "NextUpForInner 0.001 m devuelve resultado",
                () => NominalDiameters.NextUpForInner(0.001) != null);
            sb.AppendLine();

            // ================================================================
            //  Core.Common — CalcReport helpers
            // ================================================================
            sb.AppendLine("[CalcReport]");
            Check(sb, ref total, ref passed,
                "AddSegment and CountErrors",
                () =>
                {
                    var r = new CalcReport();
                    r.AddMessage(CalcWarning.Error("E1", "err"));
                    return r.CountErrors == 1;
                });
            Check(sb, ref total, ref passed,
                "Success == false when error present",
                () =>
                {
                    var r = new CalcReport();
                    r.AddMessage(CalcWarning.Error("E1", "err"));
                    return !r.Success;
                });
            sb.AppendLine();

            // Summary
            int failed = total - passed;
            sb.AppendLine("────────────────────────────────");
            sb.AppendLine((failed == 0 ? "✓ TODOS " : "✗ ") + passed + "/" + total + " tests OK"
                + (failed > 0 ? "   (" + failed + " FALLIDOS)" : ""));

            return new Report
            {
                Total    = total,
                Passed   = passed,
                Failed   = failed,
                FullText = sb.ToString(),
            };
        }

        // -- helpers ----------------------------------------------------------

        private static void Check(StringBuilder sb, ref int total, ref int passed,
            string name, Func<bool> test)
        {
            total++;
            bool ok = false;
            string err = "";
            try { ok = test(); }
            catch (Exception ex) { err = " => " + ex.Message; }
            if (ok) { passed++; sb.AppendLine("  [OK]   " + name); }
            else    { sb.AppendLine("  [FAIL] " + name + err); }
        }

        private static bool Approx(double a, double b, double tol = 1e-6)
            => Math.Abs(a - b) <= tol;

        // -- synthetic networks -----------------------------------------------

        private static PipeNetwork BuildSimplePipeNetwork()
        {
            var net = new PipeNetwork { SystemName = "Test" };
            net.SourcePressurePa = 300000.0;

            var n0 = net.CreateNode(10.0); n0.IsSource = true; net.SourceNode = n0;
            var n1 = net.CreateNode(5.0);
            var n2 = net.CreateNode(0.0); n2.IsTerminal = true; n2.DemandM3s = 0.0003;
            net.TerminalNodes.Add(n2);

            var s1 = new PipeSegment { RevitId = 1, DiameterM = 0.022, LengthM = 5.0,
                FlowM3s = 0.0003, MaterialKey = "COPPER", Roughness = 0.0000015,
                NodeStart = n0, NodeEnd = n1 };
            var s2 = new PipeSegment { RevitId = 2, DiameterM = 0.015, LengthM = 3.0,
                FlowM3s = 0.0003, MaterialKey = "COPPER", Roughness = 0.0000015,
                NodeStart = n1, NodeEnd = n2 };
            net.AddSegment(s1);
            net.AddSegment(s2);
            return net;
        }

        private static DuctNetwork BuildSimpleDuctNetwork()
        {
            var net = new DuctNetwork { SystemName = "Test" };
            net.SourcePressurePa = 250.0;

            var n0 = net.CreateNode(3.0); n0.IsSource = true; net.SourceNode = n0;
            var n1 = net.CreateNode(3.0); n1.IsTerminal = true; n1.DemandM3s = 0.1;
            net.TerminalNodes.Add(n1);

            var s1 = new DuctSegment { RevitId = 10, Shape = DuctShape.Circular,
                DiameterM = 0.25, LengthM = 6.0, FlowM3s = 0.1, Roughness = 0.0001,
                NodeStart = n0, NodeEnd = n1 };
            net.AddSegment(s1);
            return net;
        }
    }
}
