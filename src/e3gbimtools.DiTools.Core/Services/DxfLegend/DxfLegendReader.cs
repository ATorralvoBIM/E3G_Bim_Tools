using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Services.DxfLegend;

/// <summary>
/// Lee un DXF/DWG (ACadSharp) y lo aplana a primitivas de leyenda en
/// coordenadas globales y unidades de dibujo:
///   • Bloques anidados recorridos con su transformacion (punto base, escala,
///     giro, simetria).  No se usa Insert.Explode de ACadSharp: escala mal el
///     radio de los arcos de los contornos de sombreado.
///   • Capa 0 dentro de un bloque → capa del INSERT; ByBlock → color/tipo de
///     linea del INSERT.
///   • Polilineas con grosor → rellenos (troceados si su tipo de linea es
///     discontinuo) para conservar el ancho real.
///   • Hatch solido → relleno; hatch con patron → sus lineas recortadas al contorno.
///   • MTEXT → una linea de texto por renglon (ver <see cref="MTextLayout"/>).
///   • MULTILEADER → directriz + flecha + texto.
/// El orden de los elementos es el del DXF.
/// </summary>
public sealed class DxfLegendReader
{
    private const int MaxHatchLines = 20000;

    private readonly ILegendTextMeasurer _measurer;
    private CadDocument _doc = null!;
    private LegendDrawing _out = null!;
    private readonly Dictionary<string, int> _unsupported = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LegendLinePattern?> _patternCache = new(StringComparer.OrdinalIgnoreCase);
    private double _headerLtScale = 1.0;

    /// <summary>Contexto heredado del INSERT que contiene a la entidad.</summary>
    private sealed record Ctx(string Layer, LegendColor Color, LineType? LineType, double LtScale);

    public DxfLegendReader(ILegendTextMeasurer measurer)
    {
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
    }

    public LegendDrawing Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el archivo.", path);

        _doc = Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase)
            ? DwgReader.Read(path)
            : DxfReader.Read(path);
        _headerLtScale = _doc.Header.LineTypeScale > 0 ? _doc.Header.LineTypeScale : 1.0;
        _unsupported.Clear();
        _patternCache.Clear();

        _out = new LegendDrawing
        {
            FilePath = path,
            DeclaredUnits = MapUnits(_doc.Header.InsUnits),
        };

        foreach (var e in _doc.Entities)
        {
            // Rango de elementos que genera cada entidad de primer nivel: el
            // esquema de principio lo usa para saber la extension de cada bloque.
            int start = _out.Items.Count;
            Process(e, null, Affine.Identity, 0);
            _out.Sources.Add(new LegendSourceRange(e, start, _out.Items.Count - start));
        }

        foreach (var kv in _unsupported)
            _out.Warnings.Add($"{kv.Value} entidad(es) {kv.Key} no soportada(s); se omiten.");

        BuildLayerInfo();
        ComputeExtents();
        double size = Math.Max(_out.Width, _out.Height);
        _out.SuggestedUnits = size < 200 ? LegendUnits.Meters : size < 2000 ? LegendUnits.Centimeters : LegendUnits.Millimeters;
        return _out;
    }

    // ───────────────────────── Recorrido ─────────────────────────

    private void Process(Entity e, Ctx? parent, Affine t, int depth)
    {
        if (e.IsInvisible || depth > 32) return;

        var (layer, color, lineType, ltScale) = Resolve(e, parent);

        switch (e)
        {
            case Insert ins:
                ProcessInsert(ins, parent, t, depth, layer, color, lineType, ltScale);
                break;
            case Dimension dim when dim.Block is not null:
                {
                    var ctx = new Ctx(layer, color, lineType, ltScale);
                    foreach (var child in dim.Block.Entities)
                        Process(child, ctx, t, depth + 1);
                    break;
                }
            case Line l:
                {
                    var (x1, y1) = t.Apply(l.StartPoint.X, l.StartPoint.Y);
                    var (x2, y2) = t.Apply(l.EndPoint.X, l.EndPoint.Y);
                    AddLine(layer, color, Pattern(lineType, ltScale, t), x1, y1, x2, y2);
                    break;
                }
            case Arc a:
                {
                    var o = t.WithOcs(a.Normal);
                    AddArc(layer, color, Pattern(lineType, ltScale, t), o, a.Center.X, a.Center.Y, a.Radius, a.StartAngle, a.EndAngle);
                    break;
                }
            case Circle c:
                AddArc(layer, color, Pattern(lineType, ltScale, t), t.WithOcs(c.Normal), c.Center.X, c.Center.Y, c.Radius, 0, 2 * Math.PI);
                break;
            case Ellipse el:
                AddEllipse(layer, color, Pattern(lineType, ltScale, t), el, t);
                break;
            case LwPolyline pl:
                AddLwPolyline(pl, layer, color, lineType, ltScale, t.WithOcs(pl.Normal));
                break;
            case Polyline2D p2:
                AddPolyline2D(p2, layer, color, lineType, ltScale, t.WithOcs(p2.Normal));
                break;
            case Polyline3D p3:
                {
                    // Polilinea 3D: se proyecta en planta (las leyendas son 2D).
                    var pts = p3.Vertices.Select(v => t.Apply(v.Location.X, v.Location.Y)).ToList();
                    if (p3.IsClosed && pts.Count > 2) pts.Add(pts[0]);
                    var pat = Pattern(lineType, ltScale, t);
                    for (int i = 0; i + 1 < pts.Count; i++)
                        AddLine(layer, color, pat, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
                    break;
                }
            case Spline sp:
                {
                    var pts = sp.PolygonalVertexes(64).Select(p => t.Apply(p.X, p.Y)).ToList();
                    var pat = Pattern(lineType, ltScale, t);
                    for (int i = 0; i + 1 < pts.Count; i++)
                        AddLine(layer, color, pat, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
                    break;
                }
            case Hatch h:
                AddHatch(h, layer, color, t.WithOcs(h.Normal));
                break;
            case Solid s:
                {
                    var o = t.WithOcs(s.Normal);
                    var pts = new[] { s.FirstCorner, s.SecondCorner, s.FourthCorner, s.ThirdCorner };
                    AddPolygonFill(layer, color, pts.Select(p => o.Apply(p.X, p.Y)).ToList());
                    break;
                }
            case MText mt:
                AddMText(mt, layer, color, t);
                break;
            case AttributeDefinition:   // las definiciones del bloque no se dibujan (si sus ATTRIB)
            case Point:
                break;
            case TextEntity text:
                AddText(text, layer, color, t);
                break;
            case MultiLeader ml:
                AddMultiLeader(ml, layer, color, t);
                break;
            default:
                _unsupported[e.GetType().Name] = _unsupported.TryGetValue(e.GetType().Name, out var n) ? n + 1 : 1;
                break;
        }
    }

    private void ProcessInsert(Insert ins, Ctx? parent, Affine t, int depth,
        string layer, LegendColor color, LineType? lineType, double ltScale)
    {
        var ctx = new Ctx(layer, color, lineType, ltScale);

        // Los ATTRIB estan en el sistema de quien contiene al INSERT.
        foreach (var a in ins.Attributes)
            if (!a.IsInvisible && !a.Flags.HasFlag(AttributeFlags.Hidden))
            {
                var (al, ac, _, _) = Resolve(a, ctx);
                AddText(a, al, ac, t);
            }

        if (ins.Block is null) return;
        var basePt = ins.Block.BlockEntity?.BasePoint ?? XYZ.Zero;
        int rows = Math.Max(1, (int)ins.RowCount), cols = Math.Max(1, (int)ins.ColumnCount);

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var local = Affine.FromInsert(ins, basePt, c * ins.ColumnSpacing, r * ins.RowSpacing);
                var child = t.Compose(local);
                foreach (var e in ins.Block.Entities)
                    if (e is not AttributeDefinition)
                        Process(e, ctx, child, depth + 1);
            }
    }

    private (string Layer, LegendColor Color, LineType? LineType, double LtScale) Resolve(Entity e, Ctx? parent)
    {
        string layerName = e.Layer?.Name ?? "0";
        bool onLayer0 = layerName == "0";
        if (onLayer0 && parent is not null) layerName = parent.Layer;
        _doc.Layers.TryGetValue(layerName, out Layer? layer);

        LegendColor color;
        if (e.Color.IsByBlock) color = parent?.Color ?? LegendColor.Black;
        else if (e.Color.IsByLayer) color = ToRgb(layer?.Color ?? new Color(7));
        else color = ToRgb(e.Color);

        LineType? lt = e.LineType;
        string ltName = lt?.Name ?? "ByLayer";
        if (ltName.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)) lt = parent?.LineType;
        else if (ltName.Equals("ByLayer", StringComparison.OrdinalIgnoreCase)) lt = layer?.LineType;

        double lts = (e.LineTypeScale > 0 ? e.LineTypeScale : 1.0) * (parent?.LtScale ?? 1.0);
        return (layerName, color, lt, lts);
    }

    private static LegendColor ToRgb(Color c)
    {
        var rgb = c.GetRgb();
        var col = new LegendColor(rgb[0], rgb[1], rgb[2]);
        // ACI 7 / blanco puro se imprime negro sobre papel.
        return col.R == 255 && col.G == 255 && col.B == 255 ? LegendColor.Black : col;
    }

    private LegendLinePattern? Pattern(LineType? lt, double ltScale, Affine t)
    {
        if (lt is null) return null;
        var segs = lt.Segments?.ToList();
        if (segs is null || segs.Count == 0 || lt.PatternLength <= 0) return null;

        double k = _headerLtScale * ltScale;
        string key = lt.Name + "@" + k.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        if (_patternCache.TryGetValue(key, out var cached)) return cached;

        var values = new List<double>();
        foreach (var s in segs)
        {
            if (s.IsPoint) values.Add(0);
            else if (s.IsSpace || s.Length < 0) values.Add(-Math.Abs(s.Length) * k);
            else values.Add(Math.Abs(s.Length) * k);
        }
        var pattern = values.All(v => v > 0) ? null : new LegendLinePattern(lt.Name, values);
        _patternCache[key] = pattern;
        return pattern;
    }

    // ───────────────────────── Curvas ─────────────────────────

    private void AddLine(string layer, LegendColor color, LegendLinePattern? pat, double x1, double y1, double x2, double y2)
    {
        if (Math.Abs(x2 - x1) < 1e-9 && Math.Abs(y2 - y1) < 1e-9) return;
        _out.Items.Add(new LegendCurve(layer, color, pat) { Kind = LegendCurveKind.Line, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
    }

    /// <summary>Arco antihorario local (centro, radio, angulos) transformado a global.</summary>
    private void AddArc(string layer, LegendColor color, LegendLinePattern? pat, Affine t,
        double cx, double cy, double r, double a0, double a1)
    {
        var (gcx, gcy, gr, g0, g1) = t.ApplyArc(cx, cy, r, a0, a1);
        AddWorldArc(layer, color, pat, gcx, gcy, gr, g0, g1);
    }

    private void AddWorldArc(string layer, LegendColor color, LegendLinePattern? pat, double cx, double cy, double r, double a0, double a1)
    {
        if (r <= 1e-9) return;
        _out.Items.Add(new LegendCurve(layer, color, pat)
        {
            Kind = LegendCurveKind.Arc, Cx = cx, Cy = cy, R = r, A0 = a0, A1 = a1,
            X1 = cx + r * Math.Cos(a0), Y1 = cy + r * Math.Sin(a0),
            X2 = cx + r * Math.Cos(a1), Y2 = cy + r * Math.Sin(a1),
        });
    }

    private void AddEllipse(string layer, LegendColor color, LegendLinePattern? pat, Ellipse el, Affine t)
    {
        var maj = el.MajorAxisEndPoint;
        if (maj.X * maj.X + maj.Y * maj.Y <= 1e-18) return;
        double p0 = el.StartParameter, p1 = el.EndParameter;
        if (el.IsFullEllipse) { p0 = 0; p1 = 2 * Math.PI; }
        while (p1 <= p0) p1 += 2 * Math.PI;

        // El eje mayor y el centro de la ELLIPSE estan en WCS (del bloque).
        var (cx, cy) = t.Apply(el.Center.X, el.Center.Y);
        var (mx, my) = t.ApplyVec(maj.X, maj.Y);
        double rx = Math.Sqrt(mx * mx + my * my);
        double ry = rx * el.RadiusRatio;
        bool mirrored = t.Mirrored ^ (el.Normal.Z < 0);
        if (mirrored) { (p0, p1) = (-p1, -p0); while (p0 < 0) { p0 += 2 * Math.PI; p1 += 2 * Math.PI; } }

        _out.Items.Add(new LegendCurve(layer, color, pat)
        {
            Kind = LegendCurveKind.Ellipse, Cx = cx, Cy = cy,
            R = rx, R2 = ry, Rotation = Math.Atan2(my, mx), A0 = p0, A1 = p1,
        });
    }

    private void AddLwPolyline(LwPolyline pl, string layer, LegendColor color, LineType? lt, double ltScale, Affine t)
    {
        var verts = pl.Vertices
            .Select(v => (v.Location.X, v.Location.Y, v.Bulge,
                          pl.ConstantWidth > 0 ? pl.ConstantWidth : v.StartWidth,
                          pl.ConstantWidth > 0 ? pl.ConstantWidth : v.EndWidth))
            .ToList();
        AddPolylineVertices(verts, pl.IsClosed, layer, color, lt, ltScale, t);
    }

    /// <summary>POLYLINE 2D "antigua" (pre-R14): mismos datos que la LWPOLYLINE, salvo vertices de ajuste de curva.</summary>
    private void AddPolyline2D(Polyline2D pl, string layer, LegendColor color, LineType? lt, double ltScale, Affine t)
    {
        var verts = pl.Vertices
            .Where(v => !v.Flags.HasFlag(VertexFlags.SplineFrameControlPoint))
            .Select(v => (v.Location.X, v.Location.Y, v.Bulge,
                          v.StartWidth > 0 ? v.StartWidth : pl.StartWidth,
                          v.EndWidth > 0 ? v.EndWidth : pl.EndWidth))
            .ToList();
        AddPolylineVertices(verts, pl.IsClosed, layer, color, lt, ltScale, t);
    }

    private void AddPolylineVertices(
        List<(double X, double Y, double Bulge, double StartWidth, double EndWidth)> verts, bool closed,
        string layer, LegendColor color, LineType? lt, double ltScale, Affine t)
    {
        int n = verts.Count;
        if (n < 2) return;
        int segCount = closed ? n : n - 1;
        var pattern = Pattern(lt, ltScale, t);
        double ws = t.Scale;

        for (int i = 0; i < segCount; i++)
        {
            var v0 = verts[i];
            var v1 = verts[(i + 1) % n];
            double lx1 = v0.X, ly1 = v0.Y, lx2 = v1.X, ly2 = v1.Y;
            if (Math.Abs(lx2 - lx1) < 1e-12 && Math.Abs(ly2 - ly1) < 1e-12) continue;

            double w0 = v0.StartWidth * ws;
            double w1 = v0.EndWidth * ws;
            double width = Math.Max(w0, w1);

            if (Math.Abs(v0.Bulge) < 1e-9)
            {
                var (x1, y1) = t.Apply(lx1, ly1);
                var (x2, y2) = t.Apply(lx2, ly2);
                if (width > 1e-9) AddBand(layer, color, pattern, x1, y1, x2, y2, w0, w1);
                else AddLine(layer, color, pattern, x1, y1, x2, y2);
            }
            else
            {
                var (cx, cy, r, a0, a1) = BulgeToArc(lx1, ly1, lx2, ly2, v0.Bulge);
                var (gcx, gcy, gr, g0, g1) = t.ApplyArc(cx, cy, r, a0, a1);
                if (width > 1e-9) AddArcBand(layer, color, gcx, gcy, gr, g0, g1, (w0 + w1) / 2);
                else AddWorldArc(layer, color, pattern, gcx, gcy, gr, g0, g1);
            }
        }
    }

    public static (double Cx, double Cy, double R, double A0, double A1) BulgeToArc(
        double x1, double y1, double x2, double y2, double bulge)
    {
        double dx = x2 - x1, dy = y2 - y1;
        double chord = Math.Sqrt(dx * dx + dy * dy);
        double theta = 4 * Math.Atan(bulge);
        double r = Math.Abs(chord / (2 * Math.Sin(theta / 2)));
        // Distancia con signo del centro al punto medio de la cuerda (a la izquierda si bulge > 0).
        double h = chord * (1 - bulge * bulge) / (4 * bulge);
        double mx = (x1 + x2) / 2, my = (y1 + y2) / 2;
        double cx = mx - h * dy / chord, cy = my + h * dx / chord;
        double s = Math.Atan2(y1 - cy, x1 - cx), e = Math.Atan2(y2 - cy, x2 - cx);
        double a0 = bulge > 0 ? s : e, a1 = bulge > 0 ? e : s;
        while (a1 <= a0) a1 += 2 * Math.PI;
        return (cx, cy, r, a0, a1);
    }

    /// <summary>Tramo recto con grosor → rectangulo(s) solidos; si es discontinuo, un rectangulo por trazo.</summary>
    private void AddBand(string layer, LegendColor color, LegendLinePattern? pattern,
        double x1, double y1, double x2, double y2, double w0, double w1)
    {
        double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
        double ux = dx / len, uy = dy / len, nx = -uy, ny = ux;

        var pieces = new List<(double S0, double S1)>();
        double total = pattern?.Segments.Sum(Math.Abs) ?? 0;
        if (pattern is null || total <= 1e-12) pieces.Add((0, len));
        else
        {
            double s = 0; int i = 0;
            while (s < len - 1e-9 && i < 100000)
            {
                double seg = pattern.Segments[i % pattern.Segments.Count];
                if (seg > 0) pieces.Add((s, Math.Min(s + seg, len)));
                else if (seg == 0) pieces.Add((s, Math.Min(s + Math.Max(w0, w1) * 0.5, len)));
                s += Math.Abs(seg);
                i++;
            }
        }

        foreach (var (s0, s1) in pieces)
        {
            double ha = Lerp(w0, w1, s0 / len) / 2, hb = Lerp(w0, w1, s1 / len) / 2;
            double ax = x1 + ux * s0, ay = y1 + uy * s0, bx = x1 + ux * s1, by = y1 + uy * s1;
            _out.Items.Add(new BandFill(layer, color, Math.Max(w0, w1), (ax, ay), (bx, by), new[]
            {
                (ax - nx * ha, ay - ny * ha), (bx - nx * hb, by - ny * hb),
                (bx + nx * hb, by + ny * hb), (ax + nx * ha, ay + ny * ha),
            }));
        }
    }

    private void AddArcBand(string layer, LegendColor color, double cx, double cy, double r, double a0, double a1, double w)
    {
        double ro = r + w / 2, ri = Math.Max(r - w / 2, 1e-6);
        var loop = new List<LegendSegment>
        {
            LegendSegment.Arc(cx, cy, ro, a0, a1),
            LegendSegment.Line(cx + ro * Math.Cos(a1), cy + ro * Math.Sin(a1), cx + ri * Math.Cos(a1), cy + ri * Math.Sin(a1)),
            LegendSegment.Arc(cx, cy, ri, a0, a1),
            LegendSegment.Line(cx + ri * Math.Cos(a0), cy + ri * Math.Sin(a0), cx + ro * Math.Cos(a0), cy + ro * Math.Sin(a0)),
        };
        _out.Items.Add(new BandFill(layer, color, w, loop, cx, cy, r, a0, a1));
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // ───────────────────────── Rellenos ─────────────────────────

    private void AddPolygonFill(string layer, LegendColor color, IReadOnlyList<(double X, double Y)> pts)
    {
        var loop = new List<LegendSegment>();
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % pts.Count];
            if (Math.Abs(a.X - b.X) > 1e-12 || Math.Abs(a.Y - b.Y) > 1e-12)
                loop.Add(LegendSegment.Line(a.X, a.Y, b.X, b.Y));
        }
        if (loop.Count >= 3) _out.Items.Add(new LegendFill(layer, color, new[] { loop }));
    }

    private void AddHatch(Hatch h, string layer, LegendColor color, Affine t)
    {
        // Contornos en coordenadas locales del sombreado.
        var localLoops = new List<List<LegendSegment>>();
        foreach (var path in h.Paths)
        {
            var segs = new List<LegendSegment>();
            foreach (var edge in path.Edges)
                AppendEdge(edge, segs);
            if (segs.Count > 0) localLoops.Add(segs);
        }
        if (localLoops.Count == 0) return;

        // Sombreado con patron: sus lineas recortadas.  Solido o degradado: relleno del color de la entidad.
        if (!h.IsSolid && h.Pattern?.Lines?.Count > 0)
        {
            AddPatternLines(h, localLoops, layer, color, t);
            return;
        }

        var loops = localLoops.Select(l => (IReadOnlyList<LegendSegment>)l.Select(s => t.ApplySegment(s)).ToList()).ToList();
        _out.Items.Add(new LegendFill(layer, color, loops));
    }

    private static void AppendEdge(Hatch.BoundaryPath.Edge edge, List<LegendSegment> segs)
    {
        switch (edge)
        {
            case Hatch.BoundaryPath.Line l:
                segs.Add(LegendSegment.Line(l.Start.X, l.Start.Y, l.End.X, l.End.Y));
                break;
            case Hatch.BoundaryPath.Arc a:
                {
                    // Arcos horarios: los angulos se guardan reflejados (real = -guardado).
                    double a0, a1;
                    if (a.CounterClockWise) { a0 = a.StartAngle; a1 = a.EndAngle; }
                    else { a0 = -a.EndAngle; a1 = -a.StartAngle; }
                    while (a1 <= a0) a1 += 2 * Math.PI;
                    if (a1 - a0 > 2 * Math.PI + 1e-9) a1 -= 2 * Math.PI;
                    segs.Add(LegendSegment.Arc(a.Center.X, a.Center.Y, a.Radius, a0, a1));
                    break;
                }
            case Hatch.BoundaryPath.Polyline p:
                {
                    var v = p.Vertices;
                    int n = v.Count;
                    int count = p.IsClosed ? n : n - 1;
                    for (int i = 0; i < count; i++)
                    {
                        var a = v[i]; var b = v[(i + 1) % n];
                        if (Math.Abs(a.X - b.X) < 1e-12 && Math.Abs(a.Y - b.Y) < 1e-12) continue;
                        if (Math.Abs(a.Z) < 1e-9) segs.Add(LegendSegment.Line(a.X, a.Y, b.X, b.Y));
                        else
                        {
                            var (cx, cy, r, a0, a1) = BulgeToArc(a.X, a.Y, b.X, b.Y, a.Z);
                            segs.Add(LegendSegment.Arc(cx, cy, r, a0, a1));
                        }
                    }
                    break;
                }
            case Hatch.BoundaryPath.Ellipse el:
                AppendPoints(el.PolygonalVertexes(48).Select(p => (p.X, p.Y)).ToList(), segs);
                break;
            case Hatch.BoundaryPath.Spline sp:
                AppendPoints(sp.PolygonalVertexes(48).Select(p => (p.X, p.Y)).ToList(), segs);
                break;
        }
    }

    private static void AppendPoints(List<(double X, double Y)> pts, List<LegendSegment> segs)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
            if (Math.Abs(pts[i].X - pts[i + 1].X) > 1e-12 || Math.Abs(pts[i].Y - pts[i + 1].Y) > 1e-12)
                segs.Add(LegendSegment.Line(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y));
    }

    /// <summary>
    /// Genera las lineas de un patron de sombreado y las recorta al contorno
    /// (regla par-impar, como AutoCAD en modo "normal" con islas).  Las
    /// definiciones del HATCH ya vienen escaladas y giradas.
    /// </summary>
    private void AddPatternLines(Hatch h, List<List<LegendSegment>> loops, string layer, LegendColor color, Affine t)
    {
        var polys = loops.Select(l => LegendGeometry.LoopToPolygon(l, 1e-6, Math.PI / 72) ?? FlattenOpen(l)).Where(p => p.Count >= 2).ToList();
        if (polys.Count == 0) return;
        var edges = new List<(double X1, double Y1, double X2, double Y2)>();
        foreach (var p in polys)
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Count];
                edges.Add((a.X, a.Y, b.X, b.Y));
            }
        double minX = edges.Min(e => Math.Min(e.X1, e.X2)), maxX = edges.Max(e => Math.Max(e.X1, e.X2));
        double minY = edges.Min(e => Math.Min(e.Y1, e.Y2)), maxY = edges.Max(e => Math.Max(e.Y1, e.Y2));

        int emitted = 0;
        foreach (var pl in h.Pattern.Lines)
        {
            double ux = Math.Cos(pl.Angle), uy = Math.Sin(pl.Angle);
            double nx = -uy, ny = ux;
            double dn = pl.Offset.X * nx + pl.Offset.Y * ny;
            if (Math.Abs(dn) < 1e-12) continue;
            double bx = pl.BasePoint.X, by = pl.BasePoint.Y;

            // Rango de indices de linea que cubre el contorno.
            double kMin = double.MaxValue, kMax = double.MinValue;
            foreach (var (cx, cy) in new[] { (minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY) })
            {
                double k = ((cx - bx) * nx + (cy - by) * ny) / dn;
                kMin = Math.Min(kMin, k); kMax = Math.Max(kMax, k);
            }
            int k0 = (int)Math.Floor(kMin), k1 = (int)Math.Ceiling(kMax);
            if (k1 - k0 > MaxHatchLines)
            {
                _out.Warnings.Add($"Patrón '{h.Pattern.Name}' demasiado denso ({k1 - k0} líneas); se omite.");
                return;
            }

            var dashes = pl.DashLengths ?? new List<double>();
            double dashTotal = dashes.Sum(Math.Abs);

            for (int k = k0; k <= k1; k++)
            {
                double ox = bx + k * pl.Offset.X, oy = by + k * pl.Offset.Y;
                var ts = new List<double>();
                foreach (var (x1, y1, x2, y2) in edges)
                {
                    // Interseccion de la recta O + s·u con el tramo.
                    double ex = x2 - x1, ey = y2 - y1;
                    double den = ux * ey - uy * ex;
                    if (Math.Abs(den) < 1e-14) continue;
                    double wx = x1 - ox, wy = y1 - oy;
                    double s = (wx * ey - wy * ex) / den;
                    double q = (wx * uy - wy * ux) / den;
                    if (q >= 0 && q < 1) ts.Add(s);
                }
                if (ts.Count < 2) continue;
                ts.Sort();
                for (int i = 0; i + 1 < ts.Count; i += 2)
                {
                    foreach (var (s0, s1) in DashPieces(ts[i], ts[i + 1], dashes, dashTotal))
                    {
                        var (gx1, gy1) = t.Apply(ox + ux * s0, oy + uy * s0);
                        var (gx2, gy2) = t.Apply(ox + ux * s1, oy + uy * s1);
                        AddLine(layer, color, null, gx1, gy1, gx2, gy2);
                        if (++emitted > MaxHatchLines * 4)
                        {
                            _out.Warnings.Add($"Patrón '{h.Pattern.Name}' truncado por exceso de líneas.");
                            return;
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<(double, double)> DashPieces(double a, double b, List<double> dashes, double total)
    {
        if (dashes.Count == 0 || total <= 1e-12) { yield return (a, b); yield break; }
        // El patron arranca en el origen de la linea (s = 0).
        double start = Math.Floor(a / total) * total;
        for (double s = start; s < b; s += total)
        {
            double pos = s;
            foreach (var d in dashes)
            {
                double len = Math.Abs(d);
                if (d > 0)
                {
                    double p0 = Math.Max(pos, a), p1 = Math.Min(pos + len, b);
                    if (p1 - p0 > 1e-9) yield return (p0, p1);
                }
                pos += len;
            }
        }
    }

    private static List<(double X, double Y)> FlattenOpen(List<LegendSegment> loop)
    {
        var pts = new List<(double X, double Y)>();
        foreach (var s in loop)
        {
            if (s.Kind == LegendSegmentKind.Line) { pts.Add((s.X1, s.Y1)); pts.Add((s.X2, s.Y2)); }
            else pts.AddRange(LegendGeometry.ArcPoints(s.Cx, s.Cy, s.R, s.A0, s.A1));
        }
        return pts;
    }

    // ───────────────────────── Textos ─────────────────────────

    private (string Family, bool Bold, bool Italic, double Width) FontOf(TextStyle? style)
    {
        var (family, bold, italic) = _measurer.ResolveFont(style?.Filename);
        double w = style is not null && style.Width > 0 ? style.Width : 1.0;
        return (family, bold, italic, w);
    }

    private void AddText(TextEntity te, string layer, LegendColor color, Affine t)
    {
        var text = MTextLayout.ReplaceSpecials(te.Value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(text)) return;

        var (family, bold, italic, styleWidth) = FontOf(te.Style);
        double h = (te.Height > 0 ? te.Height : (te.Style?.Height > 0 ? te.Style.Height : 0.25)) * t.Scale;
        double wf = (te.WidthFactor > 0 ? te.WidthFactor : 1.0) * styleWidth;

        var ha = te.HorizontalAlignment;
        var va = te.VerticalAlignment;
        bool useAlign = !(ha == TextHorizontalAlignment.Left && va == TextVerticalAlignmentType.Baseline)
                        && ha != TextHorizontalAlignment.Aligned && ha != TextHorizontalAlignment.Fit;
        var p = useAlign ? te.AlignmentPoint : te.InsertPoint;
        var o = t.WithOcs(te.Normal);
        var (px, py) = o.Apply(p.X, p.Y);
        double rot = o.ApplyAngle(te.Rotation);

        // Desplazamiento (en el eje del texto) desde el punto de alineacion a la linea base.
        double dy = va switch
        {
            TextVerticalAlignmentType.Bottom => _measurer.Descent(family) * h,
            TextVerticalAlignmentType.Middle => -h / 2,
            TextVerticalAlignmentType.Top    => -h,
            _ => 0,
        };
        if (ha == TextHorizontalAlignment.Middle) dy = -h / 2;

        var align = ha switch
        {
            TextHorizontalAlignment.Center or TextHorizontalAlignment.Middle => LegendTextHAlign.Center,
            TextHorizontalAlignment.Right => LegendTextHAlign.Right,
            _ => LegendTextHAlign.Left,
        };

        _out.Items.Add(new LegendText(layer, color)
        {
            Text = text,
            X = px - Math.Sin(rot) * dy,
            BaselineY = py + Math.Cos(rot) * dy,
            Height = h,
            FontFamily = family, Bold = bold, Italic = italic,
            WidthFactor = wf,
            Rotation = rot,
            HAlign = align,
            MeasuredWidth = _measurer.MeasureWidth(text, family, bold, italic, h) * wf,
        });
    }

    private void AddMText(MText mt, string layer, LegendColor color, Affine t)
    {
        var (family, bold, italic, wf) = FontOf(mt.Style);
        double h = (mt.Height > 0 ? mt.Height : (mt.Style?.Height > 0 ? mt.Style.Height : 0.25)) * t.Scale;
        var dir = mt.AlignmentPoint;
        double rot = dir.X * dir.X + dir.Y * dir.Y > 1e-12 ? Math.Atan2(dir.Y, dir.X) : mt.Rotation;
        var (ix, iy) = t.Apply(mt.InsertPoint.X, mt.InsertPoint.Y);

        AddLaidOutText(mt.Value ?? string.Empty, layer, color, family, bold, italic, wf, h,
            mt.RectangleWidth * t.Scale, mt.LineSpacing, (int)mt.AttachmentPoint, ix, iy, t.ApplyAngle(rot));
    }

    private List<MTextLayout.Line> AddLaidOutText(
        string raw, string layer, LegendColor color, string family, bool bold, bool italic, double wf, double h,
        double boxWidth, double lineSpacing, int attachment, double ix, double iy, double rot)
    {
        var lines = MTextLayout.Layout(raw, h, boxWidth, lineSpacing, attachment, _measurer, family, bold, italic, wf);
        int col = (Math.Max(1, attachment) - 1) % 3;
        var align = col switch { 0 => LegendTextHAlign.Left, 1 => LegendTextHAlign.Center, _ => LegendTextHAlign.Right };
        double cos = Math.Cos(rot), sin = Math.Sin(rot);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text)) continue;
            _out.Items.Add(new LegendText(layer, color)
            {
                Text = line.Text,
                X = ix + line.X * cos - line.BaselineY * sin,
                BaselineY = iy + line.X * sin + line.BaselineY * cos,
                Height = h,
                FontFamily = family, Bold = bold, Italic = italic,
                WidthFactor = wf,
                Rotation = rot,
                HAlign = align,
                MeasuredWidth = line.Width,
            });
        }
        return lines;
    }

    // ───────────────────────── Directrices ─────────────────────────

    private void AddMultiLeader(MultiLeader ml, string layer, LegendColor color, Affine t)
    {
        var ctx = ml.ContextData;
        if (ctx is null) return;

        var leaderColor = ml.LineColor.IsByLayer || ml.LineColor.IsByBlock ? color : ToRgb(ml.LineColor);
        double arrow = (ctx.ArrowheadSize > 0 ? ctx.ArrowheadSize : ml.ArrowheadSize) * t.Scale;

        double textWidth = 0;
        if (ctx.HasTextContents && !string.IsNullOrWhiteSpace(ctx.TextLabel))
        {
            var textColor = ctx.TextColor.IsByLayer || ctx.TextColor.IsByBlock ? color : ToRgb(ctx.TextColor);
            var (family, bold, italic, wf) = FontOf(ctx.TextStyle ?? ml.TextStyle);
            int col = ctx.TextAttachmentPoint.ToString() switch { "Center" => 2, "Right" => 3, _ => 1 };
            var (tx, ty) = t.Apply(ctx.TextLocation.X, ctx.TextLocation.Y);
            var lines = AddLaidOutText(ctx.TextLabel, layer, textColor, family, bold, italic, wf,
                (ctx.TextHeight > 0 ? ctx.TextHeight : 0.25) * t.Scale, 0, ctx.LineSpacingFactor, col,
                tx, ty, t.ApplyAngle(ctx.TextRotation));
            textWidth = lines.Count == 0 ? 0 : lines.Max(l => l.Width);
        }
        else if (ctx.HasContentsBlock)
        {
            _out.Warnings.Add("Directriz múltiple con contenido de bloque: se dibuja solo la directriz.");
        }

        // Anclaje "subrayar linea inferior" (BottomLine) o "subrayar todo" (AllLine...): la
        // linea de apoyo se prolonga bajo el texto.
        static bool IsUnderline(object a)
        {
            var s = a.ToString() ?? string.Empty;
            // Ojo: TopOfTopLine, BottomOfBottomLine… tambien acaban en "Line" y NO subrayan.
            return s.Contains("Underline", StringComparison.OrdinalIgnoreCase)
                || s.Equals("BottomLine", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("AllLine", StringComparison.OrdinalIgnoreCase);
        }
        bool underline = IsUnderline(ctx.TextLeftAttachment) || IsUnderline(ctx.TextRightAttachment);
        var (textX, textY) = t.Apply(ctx.TextLocation.X, ctx.TextLocation.Y);

        foreach (var root in ctx.LeaderRoots)
        {
            var (connX, connY) = t.Apply(root.ConnectionPoint.X, root.ConnectionPoint.Y);
            foreach (var line in root.Lines)
            {
                var pts = line.Points.Select(p => t.Apply(p.X, p.Y)).ToList();
                pts.Add((connX, connY));
                for (int i = 0; i + 1 < pts.Count; i++)
                    AddLine(layer, leaderColor, null, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
                if (pts.Count >= 2 && arrow > 0) AddArrow(layer, leaderColor, pts[0], pts[1], arrow);
            }

            var (dx, dy) = t.ApplyVec(root.Direction.X, root.Direction.Y);
            double dl = Math.Sqrt(dx * dx + dy * dy);
            if (dl < 1e-9) continue;
            double ux = dx / dl, uy = dy / dl;
            double len = ml.EnableDogleg ? root.LandingDistance * t.Scale : 0;
            // Subrayado: desde la conexion hasta el final del texto (proyectado sobre la direccion).
            if (underline && textWidth > 0)
                len = Math.Max(len, (textX - connX) * ux + (textY - connY) * uy + textWidth);
            if (len > 1e-9) AddLine(layer, leaderColor, null, connX, connY, connX + ux * len, connY + uy * len);
        }
    }

    private void AddArrow(string layer, LegendColor color, (double X, double Y) tip, (double X, double Y) next, double size)
    {
        double dx = next.X - tip.X, dy = next.Y - tip.Y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return;
        double ux = dx / len, uy = dy / len, hw = size / 6;
        double bx = tip.X + ux * size, by = tip.Y + uy * size;
        AddPolygonFill(layer, color, new[] { tip, (bx - uy * hw, by + ux * hw), (bx + uy * hw, by - ux * hw) });
    }

    // ───────────────────────── Utilidades ─────────────────────────

    private static LegendUnits? MapUnits(ACadSharp.Types.Units.UnitsType u) => u switch
    {
        ACadSharp.Types.Units.UnitsType.Meters      => LegendUnits.Meters,
        ACadSharp.Types.Units.UnitsType.Centimeters => LegendUnits.Centimeters,
        ACadSharp.Types.Units.UnitsType.Millimeters => LegendUnits.Millimeters,
        ACadSharp.Types.Units.UnitsType.Inches      => LegendUnits.Inches,
        ACadSharp.Types.Units.UnitsType.Feet        => LegendUnits.Feet,
        _ => null,
    };

    private void BuildLayerInfo()
    {
        var counts = _out.Items.GroupBy(i => i.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var kv in counts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            _doc.Layers.TryGetValue(kv.Key, out Layer? layer);
            bool hidden = layer is not null && (!layer.IsOn || layer.Flags.HasFlag(LayerFlags.Frozen) || !layer.PlotFlag);
            string upper = kv.Key.ToUpperInvariant();
            bool viewportLike = upper == "DEFPOINTS" || upper.Contains("VENTANA") || upper.Contains("VIEWPORT") || upper.Contains("VPORT");
            _out.Layers.Add(new LegendLayerInfo
            {
                Name = kv.Key,
                Color = layer is null ? LegendColor.Black : ToRgb(layer.Color),
                ItemCount = kv.Value,
                ExcludedByDefault = hidden || viewportLike,
            });
        }
    }

    private void ComputeExtents()
    {
        var excluded = new HashSet<string>(_out.Layers.Where(l => l.ExcludedByDefault).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        var b = LegendGeometry.Bounds(_out.Items.Where(i => !excluded.Contains(i.Layer))) ?? LegendGeometry.Bounds(_out.Items);
        if (b is null) return;
        (_out.MinX, _out.MinY, _out.MaxX, _out.MaxY) = b.Value;
    }

    // ───────────────────────── Transformacion ─────────────────────────

    /// <summary>Transformacion afin 2D: x' = A·x + B·y + Tx ; y' = C·x + D·y + Ty.</summary>
    private readonly record struct Affine(double A, double B, double C, double D, double Tx, double Ty)
    {
        public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);
        private static readonly Affine MirrorX = new(-1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) => (A * x + B * y + Tx, C * x + D * y + Ty);
        public (double X, double Y) ApplyVec(double x, double y) => (A * x + B * y, C * x + D * y);
        public bool Mirrored => A * D - B * C < 0;
        public double Scale => Math.Sqrt(Math.Abs(A * D - B * C));

        /// <summary>this ∘ inner: primero inner, luego this.</summary>
        public Affine Compose(Affine inner) => new(
            A * inner.A + B * inner.C, A * inner.B + B * inner.D,
            C * inner.A + D * inner.C, C * inner.B + D * inner.D,
            A * inner.Tx + B * inner.Ty + Tx, C * inner.Tx + D * inner.Ty + Ty);

        /// <summary>Entidades con extrusion (0,0,-1): su OCS tiene el eje X invertido.</summary>
        public Affine WithOcs(XYZ normal) => normal.Z < 0 ? Compose(MirrorX) : this;

        public double ApplyAngle(double a)
        {
            var (x, y) = ApplyVec(Math.Cos(a), Math.Sin(a));
            return Math.Atan2(y, x);
        }

        /// <summary>Arco antihorario local → arco antihorario global (la simetria invierte el sentido).</summary>
        public (double Cx, double Cy, double R, double A0, double A1) ApplyArc(double cx, double cy, double r, double a0, double a1)
        {
            while (a1 <= a0) a1 += 2 * Math.PI;
            double sweep = Math.Min(a1 - a0, 2 * Math.PI);
            var (gx, gy) = Apply(cx, cy);
            double g0 = ApplyAngle(a0), g1 = ApplyAngle(a1);
            double start = Mirrored ? g1 : g0;
            return (gx, gy, r * Scale, start, start + sweep);
        }

        public LegendSegment ApplySegment(LegendSegment s)
        {
            if (s.Kind == LegendSegmentKind.Line)
            {
                var (x1, y1) = Apply(s.X1, s.Y1);
                var (x2, y2) = Apply(s.X2, s.Y2);
                return LegendSegment.Line(x1, y1, x2, y2);
            }
            var (cx, cy, r, a0, a1) = ApplyArc(s.Cx, s.Cy, s.R, s.A0, s.A1);
            return LegendSegment.Arc(cx, cy, r, a0, a1);
        }

        /// <summary>x' = P + R(θ)·S·(x − base + desplazamiento MINSERT).</summary>
        public static Affine FromInsert(Insert ins, XYZ basePt, double colOffset, double rowOffset)
        {
            double sx = ins.XScale == 0 ? 1 : ins.XScale, sy = ins.YScale == 0 ? 1 : ins.YScale;
            double cos = Math.Cos(ins.Rotation), sin = Math.Sin(ins.Rotation);
            double a = cos * sx, b = -sin * sy, c = sin * sx, d = cos * sy;
            // Desplazamiento de MINSERT en ejes del bloque girado (sin escalar).
            double ox = cos * colOffset - sin * rowOffset, oy = sin * colOffset + cos * rowOffset;
            double tx = ins.InsertPoint.X + ox - (a * basePt.X + b * basePt.Y);
            double ty = ins.InsertPoint.Y + oy - (c * basePt.X + d * basePt.Y);
            var local = new Affine(a, b, c, d, tx, ty);
            return ins.Normal.Z < 0 ? MirrorX.Compose(local) : local;
        }
    }
}

/// <summary>
/// Relleno que procede de una polilinea con grosor.  Conserva el eje y el
/// grosor para que el generador pueda dibujarlo como linea cuando el grosor en
/// papel es tan fino que Revit no pintaria la region en pantalla.
/// </summary>
public sealed class BandFill : LegendFill
{
    public BandFill(string layer, LegendColor color, double width,
        (double X, double Y) a, (double X, double Y) b, IReadOnlyList<(double X, double Y)> corners)
        : base(layer, color, new[] { Polygon(corners) })
    {
        BandWidth = width;
        A = a;
        B = b;
    }

    public BandFill(string layer, LegendColor color, double width, IReadOnlyList<LegendSegment> loop,
        double cx, double cy, double r, double a0, double a1)
        : base(layer, color, new[] { loop })
    {
        BandWidth = width;
        IsArc = true;
        Cx = cx; Cy = cy; R = r; A0 = a0; A1 = a1;
    }

    public double BandWidth { get; }
    /// <summary>Extremos del eje (tramo recto).</summary>
    public (double X, double Y) A { get; }
    public (double X, double Y) B { get; }
    public bool IsArc { get; }
    public double Cx { get; }
    public double Cy { get; }
    public double R { get; }
    public double A0 { get; }
    public double A1 { get; }

    private static IReadOnlyList<LegendSegment> Polygon(IReadOnlyList<(double X, double Y)> c)
    {
        var loop = new List<LegendSegment>();
        for (int i = 0; i < c.Count; i++)
        {
            var p = c[i]; var q = c[(i + 1) % c.Count];
            loop.Add(LegendSegment.Line(p.X, p.Y, q.X, q.Y));
        }
        return loop;
    }
}
