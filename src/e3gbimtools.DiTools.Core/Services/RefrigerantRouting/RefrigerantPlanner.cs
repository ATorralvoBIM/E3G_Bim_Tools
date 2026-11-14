using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;

namespace E3G.BimTools.DiTools.Core.Services.RefrigerantRouting;

/// <summary>
/// Del DXF leido + transformacion al plan de modelado (metros, coordenadas
/// internas):
///   • Cada tramo LATS lleva n tubos (liquido:gas); se desdobla en n ejes
///     paralelos separados <see cref="RefrigerantRoutingOptions.Spacing"/>.
///   • El lado del liquido se propaga por las bocas de los derivadores y cajas
///     (paridad por dispositivo) para que liquido llegue a liquido.
///   • Los tramos que acaban en una unidad interior se cortan a la entrada de su
///     recinto (+ InsideRoom), sin dejar un tramo final que no quepa el codo.
///   • Derivadores: un accesorio por tubo, desplazado al eje de ese tubo.
/// Sin dependencias de Revit (se prueba en los tests).
/// </summary>
public static class RefrigerantPlanner
{
    private sealed class Port
    {
        public LatsDevice Device = null!;
        public int Index;
        public P2 At;
        public P2 Side;
    }

    private sealed class Run
    {
        public LatsPipe Pipe = null!;
        public List<P2> Points = null!;
        public Port? A, B;
        public int? LiquidLeft;
    }

    public static RefrigerantRoutingPlan Build(LatsPlan plan, PlanTransform t, RefrigerantRoutingOptions o)
    {
        var res = new RefrigerantRoutingPlan();
        BlockRole Role(string block) =>
            o.Mappings.TryGetValue(block, out var m) ? m.Role : BlockRoleExtensions.Guess(block);

        // Bocas en coordenadas del modelo (solo dispositivos que se modelan o reciben tubos).
        var ports = new List<Port>();
        foreach (var d in plan.Devices)
        {
            // Solo unen tramos los derivadores y las cajas; la boca de una unidad
            // interior es un extremo libre (se corta en el recinto).
            if (Role(d.BlockName) is not (BlockRole.Branch or BlockRole.Unit)) continue;
            if (!plan.Blocks.TryGetValue(d.BlockName, out var def)) continue;
            foreach (var p in def.Ports)
            {
                var canon = p.Out;
                if (canon.X < -1e-9 || (Math.Abs(canon.X) <= 1e-9 && canon.Y < 0)) canon = canon * -1;
                ports.Add(new Port
                {
                    Device = d,
                    Index = p.Index,
                    At = t.Apply(d.ToWorld(p.Local)),
                    Side = t.ApplyVector(d.VectorToWorld(canon.Left)).Normalized(),
                });
            }
        }
        Port? FindPort(P2 p)
        {
            Port? best = null; double bd = o.PortTolerance;
            foreach (var q in ports)
            {
                double dd = q.At.DistanceTo(p);
                if (dd <= bd) { bd = dd; best = q; }
            }
            return best;
        }

        var runs = plan.Pipes.Where(p => p.Points.Count >= 2).Select(p =>
        {
            var pts = p.Points.Select(t.Apply).ToList();
            return new Run { Pipe = p, Points = pts, A = FindPort(pts[0]), B = FindPort(pts[^1]) };
        }).ToList();

        var parity = PropagateSides(runs, res);

        // Recintos y unidades interiores en coordenadas del modelo.
        var rooms = plan.Rooms.Select(r => r.Select(t.Apply).ToList()).ToList();
        var indoor = plan.Devices.Where(d => Role(d.BlockName) == BlockRole.IndoorUnit).Select(d => t.Apply(d.Origin)).ToList();
        bool NearIndoor(P2 p) => indoor.Any(u => u.DistanceTo(p) <= o.IndoorUnitRadius);

        foreach (var r in runs)
        {
            var pts = r.Points;
            if (o.TrimAtRoom)
            {
                if (r.B is null && NearIndoor(pts[^1]) && TrimToRoom(pts, rooms, o, out var cut)) { pts = cut; res.TrimmedAtRoom++; }
                else if (r.A is null && NearIndoor(pts[0]))
                {
                    var rev = Enumerable.Reverse(pts).ToList();
                    if (TrimToRoom(rev, rooms, o, out var cut2)) { cut2.Reverse(); pts = cut2; res.TrimmedAtRoom++; }
                }
            }

            int n = Math.Max(1, r.Pipe.Sizes.Count);
            int sign = r.LiquidLeft ?? 1;
            for (int i = 0; i < n; i++)
            {
                double off = LineOffset(i, n, o.Spacing) * sign;
                var line = Offset(pts, off);
                string label = i < r.Pipe.Sizes.Count ? r.Pipe.Sizes[i] : string.Empty;
                var rp = new RoutedPipe
                {
                    Source = r.Pipe.Handle,
                    Line = i,
                    SizeLabel = label,
                    OuterDiameterMm = ParseDiameterMm(label) ?? 0,
                    StartPort = r.A is { } a && Role(a.Device.BlockName) == BlockRole.Branch ? (a.Device.Handle, a.Index) : null,
                    EndPort = r.B is { } b && Role(b.Device.BlockName) == BlockRole.Branch ? (b.Device.Handle, b.Index) : null,
                };
                rp.Points.AddRange(line);
                if (rp.OuterDiameterMm <= 0) res.Warnings.Add($"Tramo {r.Pipe.Handle}: diámetro «{label}» no reconocido.");
                for (int k = 1; k < line.Count; k++)
                    if (line[k].DistanceTo(line[k - 1]) < 0.05)
                    { res.Warnings.Add($"Tramo {r.Pipe.Handle}: segmento de {line[k].DistanceTo(line[k - 1]) * 1000:0} mm (puede no caber el codo)."); break; }
                res.Pipes.Add(rp);
            }
        }

        // Dispositivos.
        foreach (var d in plan.Devices)
        {
            var role = Role(d.BlockName);
            if (role is BlockRole.Ignore or BlockRole.IndoorUnit) continue;
            var o0 = t.Apply(d.Origin);
            var ex = t.ApplyVector(d.VectorToWorld(new P2(1, 0))).Normalized();
            double rot = Math.Atan2(ex.Y, ex.X);
            if (role == BlockRole.Unit)
            {
                res.Devices.Add(new PlacedDevice { Handle = d.Handle, BlockName = d.BlockName, Role = role, Origin = o0, Rotation = rot, Mirrored = d.Mirrored });
                continue;
            }

            var mine = ports.Where(p => p.Device == d).ToList();
            if (mine.Count == 0) { res.Warnings.Add($"Bloque {d.BlockName} ({d.Handle}) sin bocas: no se coloca."); continue; }
            int par = parity.TryGetValue(d.Handle, out var pv) ? pv : 1;
            var connected = runs.Where(r => r.A?.Device == d || r.B?.Device == d).ToList();
            int n = connected.Count == 0 ? 2 : connected.Max(r => Math.Max(1, r.Pipe.Sizes.Count));
            for (int i = 0; i < n; i++)
            {
                var pd = new PlacedDevice
                {
                    Handle = d.Handle, BlockName = d.BlockName, Role = role, Line = i,
                    Origin = o0 + mine[0].Side * (LineOffset(i, n, o.Spacing) * par),
                    Rotation = rot, Mirrored = d.Mirrored,
                };
                foreach (var r in connected)
                {
                    var port = r.A?.Device == d ? r.A : r.B;
                    if (port is null || i >= r.Pipe.Sizes.Count) continue;
                    if (ParseDiameterMm(r.Pipe.Sizes[i]) is { } mm) pd.PortDiametersMm[port.Index] = mm;
                }
                res.Devices.Add(pd);
            }
        }
        return res;
    }

    /// <summary>Desplazamiento a la izquierda del tubo i de n (el liquido, i = 0, a la izquierda).</summary>
    public static double LineOffset(int i, int n, double spacing) => ((n - 1) / 2.0 - i) * spacing;

    /// <summary>
    /// Decide en cada tramo a que lado va el liquido (LiquidLeft, segun el sentido
    /// del tramo) y en cada dispositivo su paridad, recorriendo el grafo desde
    /// los dispositivos ya decididos.  Cada componente arranca con paridad +1.
    /// </summary>
    private static Dictionary<string, int> PropagateSides(List<Run> runs, RefrigerantRoutingPlan res)
    {
        var parity = new Dictionary<string, int>();
        var done = new HashSet<Run>();
        while (done.Count < runs.Count)
        {
            bool progress = false;
            foreach (var r in runs)
            {
                if (done.Contains(r)) continue;
                var known = new[] { (End: 0, P: r.A), (End: 1, P: r.B) }
                    .FirstOrDefault(x => x.P is not null && parity.ContainsKey(x.P.Device.Handle));
                if (known.P is null) continue;

                var liq = known.P.Side * parity[known.P.Device.Handle];
                var d0 = DirFrom(r.Points, known.End);
                int sEnd = P2.Cross(d0, liq) > 0 ? 1 : -1;      // liquido a la izquierda viniendo desde ese extremo
                r.LiquidLeft = known.End == 0 ? sEnd : -sEnd;   // respecto al sentido A→B

                foreach (var (end, p) in new[] { (0, r.A), (1, r.B) })
                {
                    if (p is null || end == known.End) continue;
                    var d1 = DirFrom(r.Points, end);
                    int s2 = end == 0 ? r.LiquidLeft.Value : -r.LiquidLeft.Value;
                    var liqv = d1.Left * s2;
                    int want = P2.Dot(liqv, p.Side) > 0 ? 1 : -1;
                    if (!parity.TryGetValue(p.Device.Handle, out var have)) parity[p.Device.Handle] = want;
                    else if (have != want)
                    {
                        res.Conflicts++;
                        res.Warnings.Add($"Tramo {r.Pipe.Handle}: el lado del líquido no cuadra en {p.Device.BlockName} ({p.Device.Handle}).");
                    }
                }
                done.Add(r);
                progress = true;
            }
            if (progress) continue;

            // Componente nueva: se fija la paridad del primer dispositivo pendiente.
            var seed = runs.FirstOrDefault(r => !done.Contains(r) && (r.A ?? r.B) is not null);
            if (seed is not null) { parity[(seed.A ?? seed.B)!.Device.Handle] = 1; continue; }
            foreach (var r in runs.Where(r => !done.Contains(r))) { r.LiquidLeft = 1; done.Add(r); }
        }
        return parity;
    }

    private static P2 DirFrom(List<P2> pts, int end) =>
        end == 0 ? (pts[1] - pts[0]).Normalized() : (pts[^2] - pts[^1]).Normalized();

    /// <summary>
    /// Corta la polilinea (el ultimo punto es el extremo libre, dentro del recinto)
    /// en el ultimo cruce con el contorno del recinto + InsideRoom.
    /// </summary>
    private static bool TrimToRoom(List<P2> pts, List<List<P2>> rooms, RefrigerantRoutingOptions o, out List<P2> result)
    {
        result = pts;
        var room = rooms.FirstOrDefault(r => Geometry2D.InPolygon(pts[^1], r));
        if (room is null) return false;

        double acc = 0, last = double.NaN;
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = pts[i].DistanceTo(pts[i - 1]);
            for (int k = 0; k < room.Count; k++)
                if (Geometry2D.Intersect(pts[i - 1], pts[i], room[k], room[(k + 1) % room.Count]) is { } tt)
                    last = double.IsNaN(last) ? acc + tt * seg : Math.Max(last, acc + tt * seg);
            acc += seg;
        }
        if (double.IsNaN(last)) return false;
        double s = Math.Min(last + o.InsideRoom, acc);

        // Sin tramo final minusculo tras un cambio de direccion.
        double a2 = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = pts[i].DistanceTo(pts[i - 1]);
            if (i > 1 && a2 < s && s < a2 + o.MinLastSegment) s = Math.Min(a2 + o.MinLastSegment, acc);
            a2 += seg;
        }

        var outPts = new List<P2> { pts[0] };
        acc = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = pts[i].DistanceTo(pts[i - 1]);
            if (acc + seg >= s - 1e-9)
            {
                double f = seg < 1e-12 ? 0 : (s - acc) / seg;
                outPts.Add(pts[i - 1] + (pts[i] - pts[i - 1]) * f);
                break;
            }
            outPts.Add(pts[i]);
            acc += seg;
        }
        if (outPts.Count < 2) return false;
        result = outPts;
        return true;
    }

    /// <summary>Polilinea paralela a <paramref name="dist"/> a la izquierda (inglete en los vertices).</summary>
    public static List<P2> Offset(IReadOnlyList<P2> pts, double dist)
    {
        var res = new List<P2>(pts.Count);
        if (Math.Abs(dist) < 1e-12) { res.AddRange(pts); return res; }
        for (int i = 0; i < pts.Count; i++)
        {
            if (i == 0 || i == pts.Count - 1)
            {
                var d = i == 0 ? (pts[1] - pts[0]).Normalized() : (pts[^1] - pts[^2]).Normalized();
                res.Add(pts[i] + d.Left * dist);
                continue;
            }
            var n1 = (pts[i] - pts[i - 1]).Normalized().Left;
            var n2 = (pts[i + 1] - pts[i]).Normalized().Left;
            var bis = (n1 + n2).Normalized();
            double cos = P2.Dot(bis, n1);
            res.Add(cos < 0.1 ? pts[i] + n1 * dist : pts[i] + bis * (dist / cos));
        }
        return res;
    }

    /// <summary>"3/8" → 9,525; "1-1/8" o "1 1/8" → 28,575; "15.88" (mm) → 15,88.</summary>
    public static double? ParseDiameterMm(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        string s = label.Trim().Replace('Ø', ' ').Replace("\"", "").Trim();
        double inches = 0;
        foreach (var part in s.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Contains('/'))
            {
                var f = part.Split('/');
                if (f.Length != 2 || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var nu)
                    || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var de) || de == 0) return null;
                inches += nu / de;
            }
            else if (double.TryParse(part.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                if (v > 3) return v; // ya en mm
                inches += v;
            }
            else return null;
        }
        return inches > 0 ? inches * 25.4 : null;
    }
}
