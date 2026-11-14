using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;

namespace E3G.BimTools.DiTools.Core.Services.RefrigerantRouting;

/// <summary>
/// Lee un DXF/DWG de trazado de tuberias de LG LATS (ACadSharp):
///   • Tramos: polilineas de la capa de tuberias; los diametros vienen en el
///     xdata LATS_PIPEINDEX ("3/8", "7/8"…: liquido y gas) o, si falta, en el
///     rotulo "3/8:7/8" mas cercano de la capa *_INDEX.
///   • Bloques LATS (derivadores, cajas, unidades interiores…): posicion y
///     bocas.  Las bocas son los pequeños trazos de la capa de tuberias que el
///     bloque lleva dentro; la boca es el extremo mas alejado del punto base.
///   • Recintos (capa de habitaciones) para cortar en la entrada.
///   • Arquitectura: el resto de lineas (sin capas ni bloques LATS), para casar
///     la planta del DXF con la de Revit.
/// No necesita contexto de Revit.
/// </summary>
public sealed class LatsPlanReader
{
    private const int MaxDepth = 8;
    private static readonly string[] NonBodyLayerHints = { "SERVICE", "INFO", "CONTROL", "AIR_FLOW", "ANNO", "TEXT", "DRAIN" };

    private readonly LatsReadOptions _opt;
    private LatsPlan _plan = null!;

    public LatsPlanReader(LatsReadOptions? options = null) => _opt = options ?? new LatsReadOptions();

    public LatsPlan Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el archivo.", path);
        CadDocument doc = Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase)
            ? DwgReader.Read(path)
            : DxfReader.Read(path);

        _plan = new LatsPlan { FilePath = path, DeclaredMetersPerUnit = MetersPerUnit(doc.Header.InsUnits) };

        var indexTexts = new List<(P2 At, string Text)>();
        foreach (var e in doc.Entities)
        {
            string layer = e.Layer?.Name ?? "0";
            switch (e)
            {
                case LwPolyline pl when Eq(layer, _opt.PipeLayer):
                    ReadPipe(pl);
                    break;
                case LwPolyline pl when Eq(layer, _opt.RoomLayer):
                    var room = pl.Vertices.Select(v => new P2(v.Location.X, v.Location.Y)).ToList();
                    if (room.Count >= 3) _plan.Rooms.Add(room);
                    break;
                case TextEntity t when layer.StartsWith(_opt.PipeLayer, StringComparison.OrdinalIgnoreCase):
                    indexTexts.Add((new P2(t.InsertPoint.X, t.InsertPoint.Y), t.Value ?? string.Empty));
                    break;
                case Insert ins when IsVendorBlock(ins):
                    ReadDevice(ins);
                    break;
                case Insert ins:
                    ReadArchitectureInsert(ins);
                    break;
                default:
                    if (!IsVendorLayer(layer)) AddArchitecture(e, Affine.Identity, layer);
                    break;
            }
        }

        FillMissingSizes(indexTexts);

        if (_plan.Pipes.Count == 0)
            _plan.Warnings.Add($"No hay polilíneas en la capa «{_opt.PipeLayer}».");
        if (_plan.Architecture.Count < 20)
            _plan.Warnings.Add("El DXF casi no tiene arquitectura: el encaje automático con Revit puede fallar; revisa el desplazamiento a mano.");
        return _plan;
    }

    // ============================================================
    // Tuberias
    // ============================================================

    private void ReadPipe(LwPolyline pl)
    {
        var pipe = new LatsPipe { Handle = pl.Handle.ToString("X") };
        foreach (var v in pl.Vertices)
        {
            var p = new P2(v.Location.X, v.Location.Y);
            if (pipe.Points.Count == 0 || pipe.Points[^1].DistanceTo(p) > 1e-6) pipe.Points.Add(p);
        }
        if (pl.IsClosed && pipe.Points.Count > 2) pipe.Points.Add(pipe.Points[0]);
        RemoveCollinear(pipe.Points);
        if (pipe.Points.Count < 2) return;

        if (pl.ExtendedData.TryGet("LATS_PIPEINDEX", out var xd))
            foreach (var r in xd.Records)
                if (r.RawValue is string s && LooksLikeSize(s))
                    pipe.Sizes.Add(s.Trim());
        _plan.Pipes.Add(pipe);
    }

    /// <summary>Sin xdata: el rotulo "liq:gas" mas cercano al tramo.</summary>
    private void FillMissingSizes(List<(P2 At, string Text)> texts)
    {
        var labels = texts.Where(t => t.Text.Contains(':')).ToList();
        foreach (var pipe in _plan.Pipes.Where(p => p.Sizes.Count == 0))
        {
            if (labels.Count == 0) { _plan.Warnings.Add($"Tramo {pipe.Handle} sin diámetros."); continue; }
            var best = labels.OrderBy(l => DistanceToPolyline(l.At, pipe.Points)).First();
            pipe.Sizes.AddRange(best.Text.Split(':').Select(s => s.Trim()).Where(LooksLikeSize));
        }
    }

    private static bool LooksLikeSize(string s)
    {
        s = s.Trim();
        return s.Length > 0 && s.Length <= 8 && char.IsDigit(s[0]) && s.All(c => char.IsDigit(c) || c is '/' or '-' or ' ' or '.' or ',');
    }

    // ============================================================
    // Bloques LATS
    // ============================================================

    private bool IsVendorBlock(Insert ins)
    {
        string name = ins.Block?.Name ?? string.Empty;
        return name.StartsWith(_opt.VendorLayerPrefix, StringComparison.OrdinalIgnoreCase)
               || IsVendorLayer(ins.Layer?.Name ?? string.Empty);
    }

    private bool IsVendorLayer(string layer) => layer.StartsWith(_opt.VendorLayerPrefix, StringComparison.OrdinalIgnoreCase);

    private void ReadDevice(Insert ins)
    {
        if (ins.Block is null) return;
        string name = ins.Block.Name ?? string.Empty;
        string upper = name.ToUpperInvariant();
        if (upper.Contains("ANNO") || upper.Contains("FLOORBASE")) return;

        if (!_plan.Blocks.TryGetValue(name, out var def))
        {
            def = ReadBlockDef(ins.Block);
            _plan.Blocks[name] = def;
        }
        def.Count++;

        var basePt = ins.Block.BlockEntity?.BasePoint ?? CSMath.XYZ.Zero;
        var f = Affine.FromInsert(ins, basePt);
        // El bloque se describe respecto a su punto base: el origen del dispositivo es la imagen del punto base.
        var o = f.Apply(new P2(basePt.X, basePt.Y));
        _plan.Devices.Add(new LatsDevice
        {
            Handle = ins.Handle.ToString("X"),
            BlockName = name,
            Layer = ins.Layer?.Name ?? string.Empty,
            A = f.A, B = f.B, C = f.C, D = f.D,
            Origin = o,
        });
    }

    private LatsBlockDef ReadBlockDef(BlockRecord block)
    {
        var def = new LatsBlockDef { Name = block.Name ?? string.Empty };
        var bp = block.BlockEntity?.BasePoint ?? CSMath.XYZ.Zero;
        var basePt = new P2(bp.X, bp.Y);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Grow(double x, double y)
        {
            minX = Math.Min(minX, x - basePt.X); minY = Math.Min(minY, y - basePt.Y);
            maxX = Math.Max(maxX, x - basePt.X); maxY = Math.Max(maxY, y - basePt.Y);
        }

        foreach (var e in block.Entities)
        {
            string layer = e.Layer?.Name ?? "0";
            if (e is Line ln && Eq(layer, _opt.PipeLayer))
            {
                var a = new P2(ln.StartPoint.X, ln.StartPoint.Y) - basePt;
                var b = new P2(ln.EndPoint.X, ln.EndPoint.Y) - basePt;
                var (near, far) = a.Length <= b.Length ? (a, b) : (b, a);
                if ((far - near).Length < 1e-9) continue;
                def.Ports.Add(new LatsPort { Index = def.Ports.Count, Local = far, Out = (far - near).Normalized() });
                continue;
            }
            if (e is TextEntity or MText or Hatch) continue;
            if (NonBodyLayerHints.Any(h => layer.ToUpperInvariant().Contains(h))) continue;
            switch (e)
            {
                case Line l: Grow(l.StartPoint.X, l.StartPoint.Y); Grow(l.EndPoint.X, l.EndPoint.Y); break;
                case LwPolyline p: foreach (var v in p.Vertices) Grow(v.Location.X, v.Location.Y); break;
                case Circle c: Grow(c.Center.X - c.Radius, c.Center.Y - c.Radius); Grow(c.Center.X + c.Radius, c.Center.Y + c.Radius); break;
            }
        }
        if (minX < maxX && minY < maxY) { def.MinX = minX; def.MinY = minY; def.MaxX = maxX; def.MaxY = maxY; }
        return def;
    }

    // ============================================================
    // Arquitectura
    // ============================================================

    private void ReadArchitectureInsert(Insert ins)
    {
        if (ins.Block is null) return;
        int before = _plan.Architecture.Count;
        var basePt = ins.Block.BlockEntity?.BasePoint ?? CSMath.XYZ.Zero;
        var f = Affine.FromInsert(ins, basePt);
        string layer = ins.Layer?.Name ?? "0";
        VisitBlock(ins.Block, f, layer, 1);

        // Candidata: la planta que LATS mete como bloque conserva dentro las coordenadas de origen.
        if (_plan.Architecture.Count - before >= 50 && f.TryInverse(out var inv))
            _plan.ArchitectureInserts.Add((ins.Block.Name ?? "?", inv));
    }

    private void VisitBlock(BlockRecord block, Affine f, string parentLayer, int depth)
    {
        if (depth > MaxDepth) return;
        foreach (var e in block.Entities)
        {
            string layer = e.Layer?.Name ?? "0";
            if (layer == "0") layer = parentLayer;
            if (IsVendorLayer(layer)) continue;
            if (e is Insert nested)
            {
                if (nested.Block is null || IsVendorBlock(nested)) continue;
                var bp = nested.Block.BlockEntity?.BasePoint ?? CSMath.XYZ.Zero;
                VisitBlock(nested.Block, f.Compose(Affine.FromInsert(nested, bp)), layer, depth + 1);
            }
            else AddArchitecture(e, f, layer);
        }
    }

    private void AddArchitecture(Entity e, Affine f, string layer)
    {
        void Seg(double x1, double y1, double x2, double y2)
        {
            var a = f.Apply(new P2(x1, y1));
            var b = f.Apply(new P2(x2, y2));
            if (a.DistanceTo(b) > 1e-9) _plan.Architecture.Add(new Seg2(a, b));
        }
        switch (e)
        {
            case Line l:
                Seg(l.StartPoint.X, l.StartPoint.Y, l.EndPoint.X, l.EndPoint.Y);
                break;
            case LwPolyline p:
            {
                var v = p.Vertices.ToList();
                for (int i = 1; i < v.Count; i++) Seg(v[i - 1].Location.X, v[i - 1].Location.Y, v[i].Location.X, v[i].Location.Y);
                if (p.IsClosed && v.Count > 2) Seg(v[^1].Location.X, v[^1].Location.Y, v[0].Location.X, v[0].Location.Y);
                break;
            }
        }
    }

    // ============================================================
    // Utilidades
    // ============================================================

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void RemoveCollinear(List<P2> pts)
    {
        for (int i = pts.Count - 2; i >= 1; i--)
        {
            var d1 = (pts[i] - pts[i - 1]).Normalized();
            var d2 = (pts[i + 1] - pts[i]).Normalized();
            if (Math.Abs(P2.Cross(d1, d2)) < 1e-6 && P2.Dot(d1, d2) > 0) pts.RemoveAt(i);
        }
    }

    internal static double DistanceToPolyline(P2 p, IReadOnlyList<P2> pts)
    {
        double best = double.MaxValue;
        for (int i = 1; i < pts.Count; i++) best = Math.Min(best, Geometry2D.DistanceToSegment(p, pts[i - 1], pts[i]));
        return best;
    }

    private static double? MetersPerUnit(ACadSharp.Types.Units.UnitsType u) => u switch
    {
        ACadSharp.Types.Units.UnitsType.Meters => 1,
        ACadSharp.Types.Units.UnitsType.Centimeters => 0.01,
        ACadSharp.Types.Units.UnitsType.Millimeters => 0.001,
        ACadSharp.Types.Units.UnitsType.Inches => 0.0254,
        ACadSharp.Types.Units.UnitsType.Feet => 0.3048,
        _ => null,
    };

    /// <summary>x' = M·x + t en planta.</summary>
    internal readonly record struct Affine(double A, double B, double C, double D, double Tx, double Ty)
    {
        public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);
        public P2 Apply(P2 p) => new(A * p.X + B * p.Y + Tx, C * p.X + D * p.Y + Ty);

        public Affine Compose(Affine inner) => new(
            A * inner.A + B * inner.C, A * inner.B + B * inner.D,
            C * inner.A + D * inner.C, C * inner.B + D * inner.D,
            A * inner.Tx + B * inner.Ty + Tx, C * inner.Tx + D * inner.Ty + Ty);

        public static Affine FromInsert(Insert ins, CSMath.XYZ basePt)
        {
            double sx = ins.XScale == 0 ? 1 : ins.XScale, sy = ins.YScale == 0 ? 1 : ins.YScale;
            double cos = Math.Cos(ins.Rotation), sin = Math.Sin(ins.Rotation);
            double a = cos * sx, b = -sin * sy, c = sin * sx, d = cos * sy;
            var f = new Affine(a, b, c, d,
                ins.InsertPoint.X - (a * basePt.X + b * basePt.Y),
                ins.InsertPoint.Y - (c * basePt.X + d * basePt.Y));
            return ins.Normal.Z < 0 ? new Affine(-1, 0, 0, 1, 0, 0).Compose(f) : f;
        }

        /// <summary>Inversa como PlanTransform; solo giro + escala uniforme sin simetria.</summary>
        public bool TryInverse(out PlanTransform inverse)
        {
            inverse = PlanTransform.Identity;
            double det = A * D - B * C;
            if (det <= 0) return false;
            double s = Math.Sqrt(det);
            if (Math.Abs(A - D) > 1e-6 * s || Math.Abs(B + C) > 1e-6 * s) return false;
            double theta = Math.Atan2(C, A);
            double c = Math.Cos(-theta), sn = Math.Sin(-theta);
            // local = (1/s)·R(-θ)·(world - t)
            double tx = -(c * Tx - sn * Ty) / s, ty = -(sn * Tx + c * Ty) / s;
            inverse = new PlanTransform(1 / s, -theta, tx, ty);
            return true;
        }
    }
}

/// <summary>Geometria en planta comun al motor.</summary>
public static class Geometry2D
{
    public static double DistanceToSegment(P2 p, P2 a, P2 b)
    {
        var ab = b - a;
        double l2 = P2.Dot(ab, ab);
        if (l2 < 1e-18) return p.DistanceTo(a);
        double t = Math.Clamp(P2.Dot(p - a, ab) / l2, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    public static P2 ClosestOnSegment(P2 p, P2 a, P2 b)
    {
        var ab = b - a;
        double l2 = P2.Dot(ab, ab);
        if (l2 < 1e-18) return a;
        double t = Math.Clamp(P2.Dot(p - a, ab) / l2, 0, 1);
        return a + ab * t;
    }

    public static bool InPolygon(P2 p, IReadOnlyList<P2> poly)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) c = !c;
        }
        return c;
    }

    /// <summary>Parametro t (0..1) sobre p1→p2 del corte con p3→p4, o null.</summary>
    public static double? Intersect(P2 p1, P2 p2, P2 p3, P2 p4)
    {
        var r = p2 - p1; var s = p4 - p3;
        double d = P2.Cross(r, s);
        if (Math.Abs(d) < 1e-15) return null;
        double t = P2.Cross(p3 - p1, s) / d;
        double u = P2.Cross(p3 - p1, r) / d;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? t : null;
    }
}
