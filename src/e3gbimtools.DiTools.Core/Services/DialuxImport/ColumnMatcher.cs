using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.BimTools.DiTools.Core.Services.DialuxImport;

/// <summary>
/// Encaja dos nubes de puntos en planta (pilares del DWG y pilares de Revit)
/// con un giro + traslacion: q ≈ R(θ)·p + t.  Los conjuntos no tienen por que
/// ser iguales (DIALux suele llevar solo una parte de la planta y Revit puede
/// tener pilares de mas), asi que se busca la transformacion que mas puntos
/// casa dentro de la tolerancia (votacion), y luego se afina con la media.
///
/// Giros candidatos: los rectos (0/90/180/270) y los que salen de comparar la
/// direccion de parejas de puntos con la misma distancia en ambos conjuntos.
/// Sin dependencias de Revit (se prueba en los tests).
/// </summary>
public static class ColumnMatcher
{
    public readonly record struct Result(double RotationRad, double Tx, double Ty, int Matched, double MeanError);

    public static Result? Match(IReadOnlyList<(double X, double Y)> dwg, IReadOnlyList<(double X, double Y)> revit, double tolerance)
    {
        var a = Dedupe(dwg, tolerance / 4);
        var b = Dedupe(revit, tolerance / 4);
        if (a.Count < 3 || b.Count < 3) return null;

        var grid = new Grid(b, tolerance);
        var sample = Spread(a, 16);

        Result? best = null;
        foreach (double theta in CandidateAngles(a, b, tolerance))
        {
            double cos = Math.Cos(theta), sin = Math.Sin(theta);
            var ra = a.Select(p => (X: cos * p.X - sin * p.Y, Y: sin * p.X + cos * p.Y)).ToList();
            var rs = sample.Select(p => (X: cos * p.X - sin * p.Y, Y: sin * p.X + cos * p.Y)).ToList();

            foreach (var p in rs)
                foreach (var q in b)
                {
                    double tx = q.X - p.X, ty = q.Y - p.Y;
                    var r = Score(ra, grid, tx, ty, tolerance);
                    if (r.Matched < 3) continue;
                    if (best is null || r.Matched > best.Value.Matched
                        || (r.Matched == best.Value.Matched && r.MeanError < best.Value.MeanError - 1e-9))
                        best = Refine(ra, grid, theta, tx, ty, tolerance);
                }
        }
        return best;
    }

    private static (int Matched, double MeanError) Score(List<(double X, double Y)> pts, Grid grid, double tx, double ty, double tol)
    {
        int n = 0;
        double err = 0;
        foreach (var p in pts)
        {
            double d = grid.Nearest(p.X + tx, p.Y + ty, tol, out _);
            if (d <= tol) { n++; err += d; }
        }
        return (n, n == 0 ? double.MaxValue : err / n);
    }

    /// <summary>Traslacion media de los casados (dos pasadas) para quitar el error del punto semilla.</summary>
    private static Result Refine(List<(double X, double Y)> pts, Grid grid, double theta, double tx, double ty, double tol)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            double sx = 0, sy = 0;
            int n = 0;
            foreach (var p in pts)
                if (grid.Nearest(p.X + tx, p.Y + ty, tol, out var q) <= tol)
                {
                    sx += q.X - p.X;
                    sy += q.Y - p.Y;
                    n++;
                }
            if (n == 0) break;
            tx = sx / n;
            ty = sy / n;
        }
        var s = Score(pts, grid, tx, ty, tol);
        return new Result(theta, tx, ty, s.Matched, s.MeanError);
    }

    private static IEnumerable<double> CandidateAngles(List<(double X, double Y)> a, List<(double X, double Y)> b, double tol)
    {
        var angles = new List<double> { 0, Math.PI / 2, Math.PI, -Math.PI / 2 };

        // Parejas lejanas del DWG contra parejas de Revit con la misma longitud.
        var seeds = Spread(a, 6);
        var votes = new Dictionary<int, int>();
        for (int i = 0; i < seeds.Count; i++)
            for (int j = i + 1; j < seeds.Count; j++)
            {
                double dx = seeds[j].X - seeds[i].X, dy = seeds[j].Y - seeds[i].Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 10 * tol) continue;
                double angA = Math.Atan2(dy, dx);
                for (int k = 0; k < b.Count; k++)
                    for (int l = 0; l < b.Count; l++)
                    {
                        if (k == l) continue;
                        double ex = b[l].X - b[k].X, ey = b[l].Y - b[k].Y;
                        if (Math.Abs(Math.Sqrt(ex * ex + ey * ey) - len) > tol) continue;
                        int key = (int)Math.Round(Normalize(Math.Atan2(ey, ex) - angA) * 180 / Math.PI * 10);
                        votes[key] = votes.TryGetValue(key, out var v) ? v + 1 : 1;
                    }
            }

        foreach (var kv in votes.OrderByDescending(kv => kv.Value).Take(12))
        {
            double ang = kv.Key / 10.0 * Math.PI / 180;
            if (angles.All(x => Math.Abs(Normalize(x - ang)) > 0.2 * Math.PI / 180)) angles.Add(ang);
        }
        return angles;
    }

    private static double Normalize(double a)
    {
        a %= 2 * Math.PI;
        if (a > Math.PI) a -= 2 * Math.PI;
        if (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    /// <summary>Hasta <paramref name="n"/> puntos repartidos (el mas alejado de los ya elegidos, cada vez).</summary>
    private static List<(double X, double Y)> Spread(List<(double X, double Y)> pts, int n)
    {
        if (pts.Count <= n) return pts.ToList();
        var chosen = new List<(double X, double Y)> { pts[0] };
        var dist = pts.Select(p => D2(p, pts[0])).ToArray();
        while (chosen.Count < n)
        {
            int far = Array.IndexOf(dist, dist.Max());
            chosen.Add(pts[far]);
            for (int i = 0; i < pts.Count; i++) dist[i] = Math.Min(dist[i], D2(pts[i], pts[far]));
        }
        return chosen;
    }

    private static double D2((double X, double Y) a, (double X, double Y) b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static List<(double X, double Y)> Dedupe(IReadOnlyList<(double X, double Y)> pts, double eps)
    {
        var res = new List<(double X, double Y)>();
        foreach (var p in pts)
            if (res.All(q => D2(p, q) > eps * eps)) res.Add(p);
        return res;
    }

    /// <summary>Rejilla hash para buscar el punto mas cercano en O(1).</summary>
    private sealed class Grid
    {
        private readonly double _cell;
        private readonly Dictionary<(long, long), List<(double X, double Y)>> _cells = new();

        public Grid(IEnumerable<(double X, double Y)> pts, double cell)
        {
            _cell = cell;
            foreach (var p in pts)
            {
                var k = Key(p.X, p.Y);
                if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new();
                list.Add(p);
            }
        }

        private (long, long) Key(double x, double y) => ((long)Math.Floor(x / _cell), (long)Math.Floor(y / _cell));

        public double Nearest(double x, double y, double maxDist, out (double X, double Y) nearest)
        {
            nearest = default;
            double best = double.MaxValue;
            var (cx, cy) = Key(x, y);
            for (long i = cx - 1; i <= cx + 1; i++)
                for (long j = cy - 1; j <= cy + 1; j++)
                    if (_cells.TryGetValue((i, j), out var list))
                        foreach (var q in list)
                        {
                            double d = Math.Sqrt((q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y));
                            if (d < best) { best = d; nearest = q; }
                        }
            return best;
        }
    }
}
