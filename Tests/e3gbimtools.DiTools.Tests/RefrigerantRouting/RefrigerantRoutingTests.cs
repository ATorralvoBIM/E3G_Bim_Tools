using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;
using E3G.BimTools.DiTools.Core.Services.RefrigerantRouting;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace E3G.BimTools.DiTools.Tests.RefrigerantRouting;

public class RefrigerantRoutingTests
{
    // DXF de LG LATS del hotel 25-144 (P01). Si no esta en la maquina los tests que lo usan no hacen nada.
    private const string LgDxf = @"C:\Users\Alopez\Downloads\DXF_Tuberias LG.dxf";
    private readonly ITestOutputHelper _out;

    public RefrigerantRoutingTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData("1/4", 6.35)]
    [InlineData("3/8", 9.525)]
    [InlineData("7/8", 22.225)]
    [InlineData("1-1/8", 28.575)]
    [InlineData("1 1/8", 28.575)]
    [InlineData("15.88", 15.88)]
    public void ParseDiameter(string label, double mm) =>
        RefrigerantPlanner.ParseDiameterMm(label).Should().BeApproximately(mm, 1e-3);

    /// <summary>Planta sintetica (habitaciones con muros dobles) en mm, girada y desplazada respecto a Revit.</summary>
    [Fact]
    public void Aligner_RecoversRotatedMillimetreDrawing()
    {
        var revit = SyntheticWalls();
        var truth = new PlanTransform(0.001, 0.3, -12.5, 40.25);
        // DXF = inversa de truth aplicada a la planta de Revit.
        var dxf = revit.Select(s => new Seg2(Inverse(truth, s.A), Inverse(truth, s.B))).ToList();

        var t = PlanAligner.Align(dxf, revit, Array.Empty<(string, PlanTransform)>(), 0.001);
        t.Should().NotBeNull();
        _out.WriteLine(t!.Describe() + " score " + t.Score);
        t.Score.Should().BeGreaterThan(0.95);
        var probe = new P2(5000, 3000);
        t.Apply(probe).DistanceTo(truth.Apply(probe)).Should().BeLessThan(0.01);
    }

    [Fact]
    public void LgDxf_ReadAlignPlan()
    {
        if (!File.Exists(LgDxf)) return;
        var plan = new LatsPlanReader().Read(LgDxf);
        _out.WriteLine($"pipes {plan.Pipes.Count} devices {plan.Devices.Count} arch {plan.Architecture.Count} inserts {plan.ArchitectureInserts.Count}");
        foreach (var b in plan.Blocks.Values) _out.WriteLine($"  {b.Name} x{b.Count} ports {b.Ports.Count} body {b.MaxX - b.MinX:0}x{b.MaxY - b.MinY:0}");
        plan.Pipes.Should().HaveCount(59);
        plan.Pipes.Should().OnlyContain(p => p.Sizes.Count == 2);
        plan.Blocks["LATS_SPLT_2WAY"].Ports.Should().HaveCount(3);
        plan.Blocks["LATS_HPSVUNIT_3WAY_3_R32"].Ports.Should().HaveCount(5);

        // «Revit» = la planta del bloque ARQ en coordenadas del modelo (la comprobada a mano: DXF − (77,901; −7,545)).
        var truth = new PlanTransform(1, 0, -77.901, 7.545);
        var revit = plan.Architecture.Select(s => new Seg2(truth.Apply(s.A), truth.Apply(s.B))).ToList();
        var t = PlanAligner.Align(plan, revit)!;
        _out.WriteLine("align: " + t.Method + " · " + t.Describe() + $" score {t.Score:P0}");
        t.Apply(new P2(10, 20)).DistanceTo(truth.Apply(new P2(10, 20))).Should().BeLessThan(0.005);

        // Sin la pista del bloque y con Revit girado 30°: lo tiene que encontrar el encaje libre.
        var rot = new PlanTransform(1, 30 * Math.PI / 180, 5, -3);
        var revit2 = revit.Select(s => new Seg2(rot.Apply(s.A), rot.Apply(s.B))).ToList();
        var t2 = PlanAligner.Align(plan.Architecture, revit2, Array.Empty<(string, PlanTransform)>(), plan.DeclaredMetersPerUnit)!;
        _out.WriteLine("align libre: " + t2.Method + " · " + t2.Describe() + $" score {t2.Score:P0}");
        var expect = rot.Apply(truth.Apply(new P2(40, 30)));
        t2.Apply(new P2(40, 30)).DistanceTo(expect).Should().BeLessThan(0.01);

        var routing = RefrigerantPlanner.Build(plan, t, new RefrigerantRoutingOptions());
        _out.WriteLine($"plan: {routing.Pipes.Count} tubos, {routing.Devices.Count} accesorios, recortados {routing.TrimmedAtRoom}, conflictos {routing.Conflicts}, {routing.TotalLength:0.0} m");
        foreach (var w in routing.Warnings.Take(10)) _out.WriteLine("  ! " + w);
        routing.Pipes.Should().HaveCount(118);
        routing.Conflicts.Should().Be(0);
        routing.TrimmedAtRoom.Should().Be(31);
        routing.Devices.Count(d => d.Role == BlockRole.Branch).Should().Be(48);
        routing.Devices.Count(d => d.Role == BlockRole.Unit).Should().Be(4);

        // Mismo resultado que el prototipo (Python) con el que se modelo el 25-144 el 2026-11-12.
        string proto = Path.Combine(Path.GetTempPath(), "claude", "6bc1c96782e40692", "11638999-dd91-450a-9519-7ab125088bda", "scratchpad", "lg_model.json");
        if (!File.Exists(proto)) return;
        using var js = JsonDocument.Parse(File.ReadAllText(proto));
        int compared = 0; double worst = 0;
        var protoRuns = js.RootElement.GetProperty("runs").EnumerateArray()
            .Select(r => (Src: r.GetProperty("src").GetString()!, Line: r.GetProperty("sys").GetString() == "L" ? 0 : 1,
                          Pts: r.GetProperty("pts").EnumerateArray().Select(p => new P2(p[0].GetDouble(), p[1].GetDouble())).ToList()))
            .ToList();
        // El lado del liquido de cada rama se elige al empezar a recorrerla: se admite el intercambio liquido/gas por tramo.
        double Dev(List<P2> a, List<P2> b) => a.Count != b.Count ? double.MaxValue : a.Zip(b, (x, y) => x.DistanceTo(y)).Max();
        foreach (var g in protoRuns.GroupBy(r => r.Src))
        {
            var p0 = g.Single(r => r.Line == 0).Pts; var p1 = g.Single(r => r.Line == 1).Pts;
            var m0 = routing.Pipes.Single(p => p.Source == g.Key && p.Line == 0).Points;
            var m1 = routing.Pipes.Single(p => p.Source == g.Key && p.Line == 1).Points;
            double d = Math.Min(Math.Max(Dev(m0, p0), Dev(m1, p1)), Math.Max(Dev(m0, p1), Dev(m1, p0)));
            if (d > 0.006) _out.WriteLine($"  dif {g.Key}: {d * 1000:0} mm");
            worst = Math.Max(worst, d);
            compared += 2;
        }
        _out.WriteLine($"comparados {compared} con el prototipo, desviación máxima {worst * 1000:0.0} mm");
        worst.Should().BeLessThan(0.006);
    }

    private static P2 Inverse(PlanTransform t, P2 q)
    {
        double c = Math.Cos(-t.Rotation), s = Math.Sin(-t.Rotation);
        double x = q.X - t.Tx, y = q.Y - t.Ty;
        return new P2((c * x - s * y) / t.Scale, (s * x + c * y) / t.Scale);
    }

    private static List<Seg2> SyntheticWalls()
    {
        var segs = new List<Seg2>();
        void Room(double x, double y, double w, double h)
        {
            foreach (double e in new[] { 0.0, 0.15 })
            {
                var a = new P2(x - e, y - e); var b = new P2(x + w + e, y - e);
                var c = new P2(x + w + e, y + h + e); var d = new P2(x - e, y + h + e);
                segs.Add(new Seg2(a, b)); segs.Add(new Seg2(b, c)); segs.Add(new Seg2(c, d)); segs.Add(new Seg2(d, a));
            }
        }
        // Irregular a proposito para que solo haya un encaje.
        Room(0, 0, 4.2, 3.1); Room(4.5, 0, 3.3, 3.1); Room(8.1, 0, 5.7, 3.1);
        Room(0, 3.4, 6.0, 4.4); Room(6.3, 3.4, 2.2, 1.9); Room(8.8, 3.4, 5.0, 6.2);
        Room(-3.0, 8.1, 2.4, 2.0);
        return segs;
    }
}
