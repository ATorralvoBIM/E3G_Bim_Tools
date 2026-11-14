using System;
using System.Collections.Generic;
using System.Linq;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Services.DxfLegend;

/// <summary>Utilidades geometricas compartidas por el lector, el generador y la vista previa.</summary>
public static class LegendGeometry
{
    /// <summary>Puntos de una curva aproximada por segmentos (para la vista previa y los contornos de respaldo).</summary>
    public static List<(double X, double Y)> Polygonize(LegendCurve c, double maxStepRad = Math.PI / 36)
    {
        switch (c.Kind)
        {
            case LegendCurveKind.Line:
                return new List<(double, double)> { (c.X1, c.Y1), (c.X2, c.Y2) };
            case LegendCurveKind.Arc:
                return ArcPoints(c.Cx, c.Cy, c.R, c.A0, c.A1, maxStepRad);
            default:
                {
                    int n = Math.Max(8, (int)Math.Ceiling((c.A1 - c.A0) / maxStepRad));
                    double cos = Math.Cos(c.Rotation), sin = Math.Sin(c.Rotation);
                    var pts = new List<(double, double)>(n + 1);
                    for (int i = 0; i <= n; i++)
                    {
                        double t = c.A0 + (c.A1 - c.A0) * i / n;
                        double lx = c.R * Math.Cos(t), ly = c.R2 * Math.Sin(t);
                        pts.Add((c.Cx + lx * cos - ly * sin, c.Cy + lx * sin + ly * cos));
                    }
                    return pts;
                }
        }
    }

    public static List<(double X, double Y)> ArcPoints(double cx, double cy, double r, double a0, double a1, double maxStepRad = Math.PI / 36)
    {
        int n = Math.Max(2, (int)Math.Ceiling((a1 - a0) / maxStepRad));
        var pts = new List<(double, double)>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double t = a0 + (a1 - a0) * i / n;
            pts.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        return pts;
    }

    /// <summary>
    /// Ordena los tramos de un contorno para que cada uno empiece donde acaba el
    /// anterior y lo devuelve como poligono.  Devuelve null si no cierra (un
    /// contorno abierto o duplicado del DXF que Revit rechazaria).
    /// </summary>
    public static List<(double X, double Y)>? LoopToPolygon(IReadOnlyList<LegendSegment> loop, double tol, double maxStepRad = Math.PI / 36)
    {
        var chained = Chain(loop, tol);
        if (chained is null) return null;
        var pts = new List<(double X, double Y)>();
        foreach (var (s, reversed) in chained)
        {
            List<(double X, double Y)> sp = s.Kind == LegendSegmentKind.Line
                ? new List<(double, double)> { (s.X1, s.Y1), (s.X2, s.Y2) }
                : ArcPoints(s.Cx, s.Cy, s.R, s.A0, s.A1, maxStepRad);
            if (reversed) sp.Reverse();
            if (pts.Count > 0) sp.RemoveAt(0);
            pts.AddRange(sp);
        }
        if (pts.Count > 1) pts.RemoveAt(pts.Count - 1); // el ultimo coincide con el primero
        return pts.Count >= 3 ? pts : null;
    }

    /// <summary>
    /// Encadena los tramos (invirtiendo los que haga falta).  Null si el
    /// contorno no es continuo y cerrado con la tolerancia indicada.
    /// </summary>
    public static List<(LegendSegment Seg, bool Reversed)>? Chain(IReadOnlyList<LegendSegment> loop, double tol)
    {
        if (loop.Count == 0) return null;
        var result = new List<(LegendSegment, bool)>(loop.Count);

        if (loop.Count == 1)
        {
            var only = loop[0];
            return Dist(only.X1, only.Y1, only.X2, only.Y2) <= tol && only.Kind == LegendSegmentKind.Arc
                ? new List<(LegendSegment, bool)> { (only, false) }
                : null;
        }

        // Sentido del primero: su final debe tocar al segundo.
        var first = loop[0];
        var second = loop[1];
        bool firstReversed = !(Touches(first.X2, first.Y2, second, tol));
        result.Add((first, firstReversed));
        var (ex, ey) = firstReversed ? (first.X1, first.Y1) : (first.X2, first.Y2);

        for (int i = 1; i < loop.Count; i++)
        {
            var s = loop[i];
            if (Dist(s.X1, s.Y1, ex, ey) <= tol) { result.Add((s, false)); (ex, ey) = (s.X2, s.Y2); }
            else if (Dist(s.X2, s.Y2, ex, ey) <= tol) { result.Add((s, true)); (ex, ey) = (s.X1, s.Y1); }
            else return null;
        }

        var (sx, sy) = firstReversed ? (first.X2, first.Y2) : (first.X1, first.Y1);
        return Dist(sx, sy, ex, ey) <= tol ? result : null;
    }

    /// <summary>
    /// True si dos tramos consecutivos se unen dando media vuelta (cuspide), p. ej.
    /// el arco grande y el pequeño del simbolo de montante, que son tangentes en el
    /// polo.  Revit acepta ese contorno pero borra la region al confirmar.
    /// </summary>
    public static bool HasCusp(List<(LegendSegment Seg, bool Reversed)> chained, double maxDeg = 12)
    {
        double limit = -Math.Cos(maxDeg * Math.PI / 180);
        for (int i = 0; i < chained.Count; i++)
        {
            var (ax, ay) = Tangent(chained[i], atEnd: true);
            var (bx, by) = Tangent(chained[(i + 1) % chained.Count], atEnd: false);
            if (ax * bx + ay * by < limit) return true;
        }
        return false;
    }

    /// <summary>Direccion unitaria de avance al principio o al final del tramo (segun su sentido en el contorno).</summary>
    private static (double X, double Y) Tangent((LegendSegment Seg, bool Reversed) c, bool atEnd)
    {
        var s = c.Seg;
        double x, y;
        if (s.Kind == LegendSegmentKind.Line)
        {
            x = s.X2 - s.X1; y = s.Y2 - s.Y1;
        }
        else
        {
            // Arco antihorario: tangente (-sen a, cos a) en el angulo a.
            bool useEndAngle = atEnd ^ c.Reversed;
            double a = useEndAngle ? s.A1 : s.A0;
            x = -Math.Sin(a); y = Math.Cos(a);
        }
        if (c.Reversed) { x = -x; y = -y; }
        double len = Math.Sqrt(x * x + y * y);
        return len < 1e-15 ? (0, 0) : (x / len, y / len);
    }

    private static bool Touches(double x, double y, LegendSegment s, double tol) =>
        Dist(x, y, s.X1, s.Y1) <= tol || Dist(x, y, s.X2, s.Y2) <= tol;

    public static double Dist(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1, dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Elimina puntos casi coincidentes y recorta las puntas en cuspide (dos
    /// tramos tangentes que se tocan en un punto, como el simbolo de montante).
    /// Revit acepta esos contornos al crear la region pero la borra al
    /// confirmar la transaccion.
    /// </summary>
    public static List<(double X, double Y)> CleanPolygon(List<(double X, double Y)> pts, double minEdge, double cuspDeg = 12)
    {
        var p = new List<(double X, double Y)>(pts);
        bool changed = true;
        int guard = 0;
        while (changed && p.Count >= 3 && guard++ < 1000)
        {
            changed = false;
            for (int i = 0; i < p.Count && p.Count >= 3; i++)
            {
                var prev = p[(i - 1 + p.Count) % p.Count];
                var cur = p[i];
                var next = p[(i + 1) % p.Count];
                if (Dist(prev.X, prev.Y, cur.X, cur.Y) < minEdge)
                {
                    p.RemoveAt(i); changed = true; break;
                }
                double ax = prev.X - cur.X, ay = prev.Y - cur.Y, bx = next.X - cur.X, by = next.Y - cur.Y;
                double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                if (la < 1e-12 || lb < 1e-12) { p.RemoveAt(i); changed = true; break; }
                double cos = (ax * bx + ay * by) / (la * lb);
                double angle = Math.Acos(Math.Max(-1, Math.Min(1, cos))) * 180 / Math.PI;
                if (angle < cuspDeg)
                {
                    p.RemoveAt(i); changed = true; break;
                }
            }
        }
        return p;
    }

    public static (double MinX, double MinY, double MaxX, double MaxY)? Bounds(IEnumerable<LegendItem> items)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        void Add(double x, double y)
        {
            any = true;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }

        foreach (var item in items)
        {
            switch (item)
            {
                case LegendCurve c:
                    foreach (var (x, y) in Polygonize(c, Math.PI / 8)) Add(x, y);
                    break;
                case LegendFill f:
                    foreach (var loop in f.Loops)
                        foreach (var s in loop)
                        {
                            if (s.Kind == LegendSegmentKind.Line) { Add(s.X1, s.Y1); Add(s.X2, s.Y2); }
                            else foreach (var (x, y) in ArcPoints(s.Cx, s.Cy, s.R, s.A0, s.A1, Math.PI / 8)) Add(x, y);
                        }
                    break;
                case LegendText t:
                    {
                        double x0 = t.HAlign switch
                        {
                            LegendTextHAlign.Center => t.X - t.MeasuredWidth / 2,
                            LegendTextHAlign.Right => t.X - t.MeasuredWidth,
                            _ => t.X,
                        };
                        Add(x0, t.BaselineY); Add(x0 + t.MeasuredWidth, t.BaselineY + t.Height);
                        break;
                    }
            }
        }
        return any ? (minX, minY, maxX, maxY) : null;
    }
}
