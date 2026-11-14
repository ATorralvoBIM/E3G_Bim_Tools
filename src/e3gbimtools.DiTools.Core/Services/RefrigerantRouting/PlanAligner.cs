using System;
using System.Collections.Generic;
using System.Linq;
using E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;

namespace E3G.BimTools.DiTools.Core.Services.RefrigerantRouting;

/// <summary>
/// Casa la planta de arquitectura del DXF con la de Revit (caras de muros en
/// metros, coordenadas internas): busca escala de unidades, giro y traslacion
/// que ponen mas lineas del DXF encima de muros de Revit.
///
/// Candidatas (todas se puntuan igual y gana la que mas casa):
///   1. La inversa de cada bloque de arquitectura del DXF: LATS inserta la planta
///      exportada de Revit como bloque y dentro conserva las coordenadas del modelo.
///   2. Las coordenadas del DXF tal cual (por si se exporto desde este Revit).
///   3. Encaje libre: giro por histograma de direcciones (modulo 90°) y
///      traslacion por votacion de esquinas.
/// La mejor se afina con ICP (punto a segmento, giro + traslacion).
/// Puntuacion: fraccion de puntos de muestra del DXF a menos de la tolerancia
/// de alguna cara de muro.  Sin dependencias de Revit (se prueba en los tests).
/// </summary>
public static class PlanAligner
{
    private static readonly double[] UnitScales = { 1, 0.001, 0.01, 0.0254, 0.3048 };
    private const int MaxSamples = 1500;

    public static PlanTransform? Align(LatsPlan plan, IReadOnlyList<Seg2> revit, double tolerance = 0.05) =>
        Align(plan.Architecture, revit, plan.ArchitectureInserts.Select(a => (a.Block, a.Inverse)), plan.DeclaredMetersPerUnit, tolerance);

    public static PlanTransform? Align(
        IReadOnlyList<Seg2> dxf, IReadOnlyList<Seg2> revit,
        IEnumerable<(string Name, PlanTransform Inverse)> hints, double? declaredMetersPerUnit, double tolerance = 0.05)
    {
        var rv = revit.Where(s => s.Length > 0.05).ToList();
        if (dxf.Count < 3 || rv.Count < 3) return null;
        var grid = new SegmentGrid(rv, 1.0);

        double dxfDiag = Diagonal(dxf), revitDiag = Diagonal(rv);
        var scales = UnitScales.Concat(declaredMetersPerUnit is { } d ? new[] { d } : Array.Empty<double>())
            .Distinct()
            .Where(k => dxfDiag * k > revitDiag / 20 && dxfDiag * k < revitDiag * 20)
            .ToList();
        if (scales.Count == 0) scales.Add(declaredMetersPerUnit ?? 1);

        var candidates = new List<PlanTransform>();
        var hintList = hints.ToList();
        foreach (double k in scales)
        {
            foreach (var (name, h) in hintList)
                candidates.Add(new PlanTransform(h.Scale * k, h.Rotation, h.Tx * k, h.Ty * k) { Method = $"Bloque de planta «{name}» del DXF" });
            candidates.Add(new PlanTransform(k, 0, 0, 0) { Method = "Coordenadas del DXF tal cual" });
            candidates.AddRange(Voting(dxf, rv, k, tolerance));
        }

        // Puntuacion con una muestra; las 6 mejores se afinan.
        var scored = new List<PlanTransform>();
        foreach (var c in candidates)
        {
            var sample = Sample(dxf, c.Scale);
            scored.Add(Score(c, sample, grid, tolerance));
        }
        PlanTransform? best = null;
        foreach (var c in scored.OrderByDescending(s => s.Score).Take(6))
        {
            var sample = Sample(dxf, c.Scale);
            var r = Score(Refine(c, sample, grid, tolerance), sample, grid, tolerance);
            if (r.Score < c.Score) r = c;
            if (best is null || r.Score > best.Score + 1e-9) best = r;
        }
        return best;
    }

    /// <summary>Puntua una transformacion dada (p. ej. la escrita a mano).</summary>
    public static PlanTransform Evaluate(PlanTransform t, IReadOnlyList<Seg2> dxf, IReadOnlyList<Seg2> revit, double tolerance = 0.05)
    {
        var rv = revit.Where(s => s.Length > 0.05).ToList();
        if (rv.Count == 0 || dxf.Count == 0) return t with { Score = double.NaN };
        return Score(t, Sample(dxf, t.Scale), new SegmentGrid(rv, 1.0), tolerance);
    }

    // ============================================================
    // Encaje libre
    // ============================================================

    private static IEnumerable<PlanTransform> Voting(IReadOnlyList<Seg2> dxf, List<Seg2> revit, double k, double tol)
    {
        double delta = DominantShift(dxf, revit);
        var dxfLong = dxf.Where(s => s.Length * k > 0.5).OrderByDescending(s => s.Length).Take(120).ToList();
        var revLong = revit.OrderByDescending(s => s.Length).Take(1500).ToList();
        var revPts = revLong.SelectMany(s => new[] { s.A, s.B }).ToList();
        if (dxfLong.Count < 3 || revPts.Count < 3) yield break;

        double bin = Math.Max(2 * tol, 0.05);
        for (int q = 0; q < 4; q++)
        {
            double theta = delta + q * Math.PI / 2;
            var probe = new PlanTransform(k, theta, 0, 0);
            var dPts = dxfLong.SelectMany(s => new[] { probe.Apply(s.A), probe.Apply(s.B) }).ToList();
            var votes = new Dictionary<(long, long), int>();
            foreach (var p in dPts)
                foreach (var r in revPts)
                {
                    var key = ((long)Math.Round((r.X - p.X) / bin), (long)Math.Round((r.Y - p.Y) / bin));
                    votes[key] = votes.TryGetValue(key, out var v) ? v + 1 : 1;
                }
            foreach (var kv in votes.OrderByDescending(v => v.Value).Take(5))
                yield return new PlanTransform(k, theta, kv.Key.Item1 * bin, kv.Key.Item2 * bin) { Method = "Encaje automático con los muros" };
        }
    }

    /// <summary>Giro (modulo 90°) que mejor superpone los histogramas de direcciones.</summary>
    private static double DominantShift(IReadOnlyList<Seg2> a, IReadOnlyList<Seg2> b)
    {
        const int bins = 360; // 0.25° en 90°
        double[] Hist(IEnumerable<Seg2> segs)
        {
            var h = new double[bins];
            foreach (var s in segs)
            {
                var d = s.B - s.A;
                double ang = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
                ang = ((ang % 90) + 90) % 90;
                h[(int)(ang / 90 * bins) % bins] += s.Length;
            }
            // Suavizado ±1 bin.
            var o = new double[bins];
            for (int i = 0; i < bins; i++) o[i] = h[(i + bins - 1) % bins] + 2 * h[i] + h[(i + 1) % bins];
            return o;
        }
        var ha = Hist(a); var hb = Hist(b);
        int bestShift = 0; double bestVal = double.MinValue;
        for (int sft = 0; sft < bins; sft++)
        {
            double v = 0;
            for (int i = 0; i < bins; i++) v += ha[i] * hb[(i + sft) % bins];
            if (v > bestVal) { bestVal = v; bestShift = sft; }
        }
        return bestShift * (Math.PI / 2) / bins;
    }

    // ============================================================
    // Puntuacion y afinado
    // ============================================================

    /// <summary>Puntos de muestra (unidades del DXF): mitad y cerca de los extremos de lineas de al menos 20 cm.</summary>
    private static List<P2> Sample(IReadOnlyList<Seg2> dxf, double scale)
    {
        var pts = new List<P2>();
        foreach (var s in dxf)
        {
            if (s.Length * scale < 0.2) continue;
            var d = s.B - s.A;
            pts.Add(s.A + d * 0.5);
            pts.Add(s.A + d * 0.15);
            pts.Add(s.A + d * 0.85);
        }
        if (pts.Count <= MaxSamples) return pts;
        double step = pts.Count / (double)MaxSamples;
        var res = new List<P2>(MaxSamples);
        for (double i = 0; i < pts.Count && res.Count < MaxSamples; i += step) res.Add(pts[(int)i]);
        return res;
    }

    private static PlanTransform Score(PlanTransform t, List<P2> sample, SegmentGrid grid, double tol)
    {
        if (sample.Count == 0) return t with { Score = 0, Matched = 0, Sampled = 0 };
        int n = 0;
        foreach (var p in sample)
            if (grid.Distance(t.Apply(p), tol, out _) <= tol) n++;
        return t with { Score = n / (double)sample.Count, Matched = n, Sampled = sample.Count };
    }

    /// <summary>ICP punto-segmento: giro + traslacion por minimos cuadrados, con radio decreciente.</summary>
    private static PlanTransform Refine(PlanTransform t, List<P2> sample, SegmentGrid grid, double tol)
    {
        foreach (double radius in new[] { 6 * tol, 4 * tol, 2 * tol, 2 * tol, tol })
        {
            var src = new List<P2>(); var dst = new List<P2>();
            foreach (var p in sample)
            {
                var q = t.Apply(p);
                if (grid.Distance(q, radius, out var r) <= radius) { src.Add(q); dst.Add(r); }
            }
            if (src.Count < 6) break;
            var cs = Mean(src); var cd = Mean(dst);
            double sxx = 0, sxy = 0;
            for (int i = 0; i < src.Count; i++)
            {
                var a = src[i] - cs; var b = dst[i] - cd;
                sxx += a.X * b.X + a.Y * b.Y;
                sxy += a.X * b.Y - a.Y * b.X;
            }
            double dth = Math.Atan2(sxy, sxx);
            if (Math.Abs(dth) > 2 * Math.PI / 180) dth = 0; // el giro ya viene del histograma: solo se retoca
            double c = Math.Cos(dth), s = Math.Sin(dth);
            var rcs = new P2(c * cs.X - s * cs.Y, s * cs.X + c * cs.Y);
            var dt = cd - rcs;
            var nt = new P2(c * t.Tx - s * t.Ty, s * t.Tx + c * t.Ty) + dt;
            t = t with { Rotation = t.Rotation + dth, Tx = nt.X, Ty = nt.Y };
        }
        return t;
    }

    private static P2 Mean(List<P2> pts)
    {
        double x = 0, y = 0;
        foreach (var p in pts) { x += p.X; y += p.Y; }
        return new P2(x / pts.Count, y / pts.Count);
    }

    private static double Diagonal(IEnumerable<Seg2> segs)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in segs)
            foreach (var p in new[] { s.A, s.B })
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }
        return minX > maxX ? 0 : Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
    }

    /// <summary>Rejilla de segmentos para la distancia punto-segmento mas corta.</summary>
    private sealed class SegmentGrid
    {
        private readonly double _cell;
        private readonly Dictionary<(long, long), List<Seg2>> _cells = new();

        public SegmentGrid(IEnumerable<Seg2> segs, double cell)
        {
            _cell = cell;
            foreach (var s in segs)
            {
                long x0 = K(Math.Min(s.A.X, s.B.X)), x1 = K(Math.Max(s.A.X, s.B.X));
                long y0 = K(Math.Min(s.A.Y, s.B.Y)), y1 = K(Math.Max(s.A.Y, s.B.Y));
                if ((x1 - x0 + 1) * (y1 - y0 + 1) > 4000) continue;
                for (long i = x0; i <= x1; i++)
                    for (long j = y0; j <= y1; j++)
                    {
                        // Solo las celdas que el segmento atraviesa (con margen de media celda).
                        var c = new P2((i + 0.5) * _cell, (j + 0.5) * _cell);
                        if (Geometry2D.DistanceToSegment(c, s.A, s.B) > _cell) continue;
                        if (!_cells.TryGetValue((i, j), out var l)) _cells[(i, j)] = l = new List<Seg2>();
                        l.Add(s);
                    }
            }
        }

        private long K(double v) => (long)Math.Floor(v / _cell);

        public double Distance(P2 p, double max, out P2 closest)
        {
            closest = p;
            double best = double.MaxValue;
            long cx = K(p.X), cy = K(p.Y);
            int reach = Math.Max(1, (int)Math.Ceiling(max / _cell));
            for (long i = cx - reach; i <= cx + reach; i++)
                for (long j = cy - reach; j <= cy + reach; j++)
                {
                    if (!_cells.TryGetValue((i, j), out var l)) continue;
                    foreach (var s in l)
                    {
                        var q = Geometry2D.ClosestOnSegment(p, s.A, s.B);
                        double d = q.DistanceTo(p);
                        if (d < best) { best = d; closest = q; }
                    }
                }
            return best;
        }
    }
}
