using System;
using System.Collections.Generic;
using System.Linq;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;
using E3G.BimTools.DiTools.Core.Models.Schematic;

namespace E3G.BimTools.DiTools.Core.Services.Schematic;

/// <summary>
/// Redibuja un <see cref="SchematicDiagram"/> con estilo E3G y lo compacta para
/// que quepa en el plano sin perder informacion ni sentido.
///
/// Medidas en mm de papel.  u = mm de papel por unidad del DXF, fijado para que
/// los rotulos salgan a <see cref="SchematicStyleOptions.TextMm"/>.
///
/// Compactacion (lo que cambia son longitudes y posiciones, nunca la topologia):
///   • Cada equipo es rigido: su caja y sus puertos se escalan con u.
///   • Su punto de insercion se desplaza con factores kx, ky ≤ 1 (uno por eje),
///     asi que el orden de filas y columnas se conserva: nada cambia de lado,
///     los tramos siguen siendo ortogonales y no aparecen cruces.
///   • Los vertices intermedios de cada tramo se recolocan respecto a sus
///     extremos (ajuste exacto si coinciden con un extremo; si no, en proporcion).
///   • Rotulos de equipo: rigidos respecto al equipo.  Rotulos de tramo:
///     diametros encima y longitud debajo del segmento horizontal mas largo.
///   • kx y ky se reducen mientras no aparezcan solapes nuevos (cajas de
///     equipos, rotulos y segmentos de tuberia) respecto al dibujo original.
/// El texto NO se toca nunca (2026-11-12: se usan tipos de texto del proyecto).
/// Si no cabe en el area util, se reducen los simbolos y las distancias (hasta
/// el 50 %) y se vuelve a compactar; si ni asi cabe, se avisa.
///
/// Estilo E3G (propuesta 2026-11-12): Calibri; cada tramo se dibuja con un tubo
/// de liquido (50_12_LiquidRefrigerant) y uno o dos de gas
/// (50_13_GasRefrigerant); equipos con simbolo simplificado en negro.
/// </summary>
public sealed class SchematicComposer
{
    public static readonly LegendColor Liquid = new(191, 255, 127);
    public static readonly LegendColor Gas = new(153, 76, 76);
    private static readonly LegendColor Ink = LegendColor.Black;
    private static readonly LegendColor Aux = new(128, 128, 128);
    /// <summary>Estilo E3G de equipos: linea RGB 064-128-128 y region rellena RGB 192-192-192.</summary>
    public static readonly LegendColor EquipmentLine = new(64, 128, 128);
    public static readonly LegendColor EquipmentFill = new(192, 192, 192);
    private static readonly LegendColor PipeText = new(64, 64, 64);
    private static readonly LegendColor Warn = new(192, 0, 0);

    public const string LiquidStyle = "50_12_LiquidRefrigerant";
    public const string GasStyle = "50_13_GasRefrigerant";
    public const string EquipmentStyle = "E3G_ESQ_Equipos";
    public const string AuxStyle = "E3G_ESQ_Auxiliar";

    private const string LayEq = "Equipos";
    private const string LayEqFill = "Equipos (relleno)";
    private const string LayLiq = "Tubería líquido";
    private const string LayGas = "Tubería gas";
    private const string LayEqText = "Rótulos de equipos";
    private const string LayPipeText = "Rótulos de tuberías";
    private const string LayTitle = "Cartela";

    private const double TubeSpacing = 0.7;   // mm entre tubos de un mismo tramo
    private const double Clearance = 0.75;    // holgura (mm) al comprobar solapes

    private readonly ILegendTextMeasurer _measurer;
    private SchematicDiagram _d = null!;
    private SchematicStyleOptions _o = null!;
    private double _u, _c, _ct, _tolSrc;

    public SchematicComposer(ILegendTextMeasurer measurer)
    {
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
    }

    public SchematicComposition Compose(SchematicDiagram diagram, SchematicStyleOptions options)
    {
        if (!diagram.IsRecognized) throw new InvalidOperationException("El esquema no se ha reconocido: no se puede aplicar el estilo E3G.");
        _d = diagram;
        _o = options;
        _c = options.TextMm;
        _ct = options.TitleTextMm > 0 ? options.TitleTextMm : _c * 1.75;
        _tolSrc = diagram.TextHeight * 0.05;
        double uBase = options.SymbolMm / (diagram.TextHeight > 0 ? diagram.TextHeight : 1);

        // Simbolos y distancias a escala s (texto fijo): de 100 % hacia abajo
        // hasta que quepa en el area util.
        (List<Prim> Prims, SchematicBox Box, double Kx, double Ky, double S)? best = null;
        bool fits = false;
        foreach (double sc in new[] { 1.0, 0.9, 0.8, 0.7, 0.6, 0.5 })
        {
            _u = uBase * sc;
            double kx = 1, ky = 1;
            if (options.Compact)
            {
                var baseline = Conflicts(Layout(1, 1));
                ky = Shrink(k => Layout(1, k), baseline);
                kx = Shrink(k => Layout(k, ky), baseline);
                // Repaso: al juntar columnas puede sobrar algo de altura.
                ky = Shrink(k => Layout(kx, k * ky), baseline) * ky;
            }
            var prims0 = new List<Prim>();
            Emit(Layout(kx, ky), prims0);
            EmitTitleBlock(prims0);
            var b0 = Bounds(prims0);
            best = (prims0, b0, kx, ky, sc);
            if (b0.Width <= options.PaperWidthMm + 0.5 && b0.Height <= options.PaperHeightMm + 0.5) { fits = true; break; }
            if (!options.Compact) break;
        }
        var (prims, b, kxF, kyF, sF) = best!.Value;
        double w = b.Width, h = b.Height, g = 1.0;

        var result = Build(prims, b, g);
        var comp = new SchematicComposition
        {
            Drawing = result,
            LineStyleNames = new Dictionary<string, string>
            {
                [Liquid.Key] = LiquidStyle,
                [Gas.Key] = GasStyle,
                [EquipmentLine.Key] = EquipmentStyle,
                [Aux.Key] = AuxStyle,
            },
            LineWeights = new Dictionary<string, int>
            {
                [LiquidStyle] = 4, [GasStyle] = 4, [EquipmentStyle] = 2, [AuxStyle] = 1,
            },
            WidthMm = w,
            HeightMm = h,
            CompactX = kxF,
            CompactY = kyF,
            TextMm = _c,
            Fits = fits,
        };
        comp.Notes.Add($"Distancias reducidas al {kxF * 100:0} % en horizontal y al {kyF * 100:0} % en vertical.");
        if (sF < 0.999) comp.Notes.Add($"Para caber, símbolos y distancias al {sF * 100:0} %; el texto se mantiene a {_c:0.0#} mm.");
        if (!fits) comp.Notes.Add($"No cabe en {options.PaperWidthMm:0} × {options.PaperHeightMm:0} mm con texto de {_c:0.0#} mm: elige un formato mayor o un tipo de texto más pequeño.");
        return comp;
    }

    // ============================================================
    // Disposicion para unos factores (kx, ky)
    // ============================================================

    private sealed record TextLine(string Text, bool Bold);

    /// <summary>Bloque de texto: lineas apiladas; (X, Top) segun la alineacion.</summary>
    private sealed class TextBlock
    {
        public List<TextLine> Lines { get; init; } = new();
        public double X { get; set; }
        public double Top { get; set; }
        public LegendTextHAlign Align { get; set; }
        public double Width { get; init; }
        public double Height { get; init; }
        public LegendColor Color { get; init; }
        public string Layer { get; init; } = string.Empty;
        public string Owner { get; init; } = string.Empty;
        public double Cap { get; init; }

        public SchematicBox Box
        {
            get
            {
                double x0 = Align switch { LegendTextHAlign.Center => X - Width / 2, LegendTextHAlign.Right => X - Width, _ => X };
                return new SchematicBox(x0, Top - Height, x0 + Width, Top);
            }
        }
    }

    private sealed class Placed
    {
        public SchematicEquipment Eq { get; init; } = null!;
        public double Ix { get; init; }
        public double Iy { get; init; }
        public SchematicBox Box { get; init; }
    }

    private sealed class Layouted
    {
        public Dictionary<int, Placed> Equipment { get; } = new();
        public Dictionary<int, List<(double X, double Y)>> Paths { get; } = new();
        public List<TextBlock> Texts { get; } = new();
    }

    private Layouted Layout(double kx, double ky)
    {
        var lay = new Layouted();
        foreach (var e in _d.Equipment)
        {
            double ix = e.InsertX * kx * _u, iy = e.InsertY * ky * _u;
            lay.Equipment[e.Index] = new Placed
            {
                Eq = e, Ix = ix, Iy = iy,
                Box = Rigid(e.Box, e, ix, iy),
            };
        }

        foreach (var p in _d.Pipes)
        {
            var path = PipePath(p, lay, kx, ky);
            lay.Paths[p.Index] = path;
            AddPipeLabels(p, path, lay);
        }

        foreach (var e in _d.Equipment)
            foreach (var l in e.Labels)
                lay.Texts.Add(EquipmentLabel(l, e, lay.Equipment[e.Index]));

        foreach (var l in _d.FreeLabels)
        {
            var tb = Block(l.Lines.Select(t => new TextLine(t, false)), _c, l.IsWarning ? Warn : Ink, LayEqText, "F" + l.GetHashCode());
            tb.X = l.Box.MinX * kx * _u;
            tb.Top = l.Box.MaxY * ky * _u;
            lay.Texts.Add(tb);
        }
        return lay;
    }

    private SchematicBox Rigid(SchematicBox src, SchematicEquipment e, double ix, double iy) => new(
        ix + (src.MinX - e.InsertX) * _u, iy + (src.MinY - e.InsertY) * _u,
        ix + (src.MaxX - e.InsertX) * _u, iy + (src.MaxY - e.InsertY) * _u);

    private List<(double X, double Y)> PipePath(SchematicPipe p, Layouted lay, double kx, double ky)
    {
        var src = p.Points;
        (double X, double Y) Map(int i, int? eq)
        {
            var (x, y) = src[i];
            if (eq is int id && lay.Equipment.TryGetValue(id, out var pl))
                return (pl.Ix + (x - pl.Eq.InsertX) * _u, pl.Iy + (y - pl.Eq.InsertY) * _u);
            return (x * kx * _u, y * ky * _u);
        }

        var a = Map(0, p.From);
        var b = Map(src.Count - 1, p.To);
        var s0 = src[0];
        var s1 = src[^1];

        double Coord(double v, double v0, double v1, double n0, double n1, double k)
        {
            if (Math.Abs(v - v0) <= _tolSrc) return n0;
            if (Math.Abs(v - v1) <= _tolSrc) return n1;
            if (Math.Abs(v1 - v0) > _tolSrc) return n0 + (v - v0) / (v1 - v0) * (n1 - n0);
            return n0 + (v - v0) * k * _u;
        }

        var path = new List<(double X, double Y)> { a };
        for (int i = 1; i < src.Count - 1; i++)
            path.Add((Coord(src[i].X, s0.X, s1.X, a.X, b.X, kx), Coord(src[i].Y, s0.Y, s1.Y, a.Y, b.Y, ky)));
        path.Add(b);

        // Recorta los extremos en el borde del simbolo (los puertos de LATS caen dentro).
        if (p.From is int f && path.Count >= 2) path[0] = ExitPoint(lay.Equipment[f].Box, path[1], path[0]);
        if (p.To is int t && path.Count >= 2) path[^1] = ExitPoint(lay.Equipment[t].Box, path[^2], path[^1]);
        return path;
    }

    /// <summary>Si <paramref name="inside"/> esta dentro de la caja, punto donde el segmento desde <paramref name="outside"/> la toca.</summary>
    private static (double X, double Y) ExitPoint(SchematicBox box, (double X, double Y) outside, (double X, double Y) inside)
    {
        if (!box.Contains(inside.X, inside.Y, 0) || box.Contains(outside.X, outside.Y, 0)) return inside;
        double dx = inside.X - outside.X, dy = inside.Y - outside.Y;
        // El primer corte que cae sobre el borde de la caja.
        var cands = new List<double>();
        if (Math.Abs(dx) > 1e-12) { cands.Add((box.MinX - outside.X) / dx); cands.Add((box.MaxX - outside.X) / dx); }
        if (Math.Abs(dy) > 1e-12) { cands.Add((box.MinY - outside.Y) / dy); cands.Add((box.MaxY - outside.Y) / dy); }
        foreach (var c in cands.Where(c => c >= 0 && c <= 1).OrderBy(c => c))
        {
            double x = outside.X + dx * c, y = outside.Y + dy * c;
            if (box.Contains(x, y, 1e-6)) return (x, y);
        }
        return inside;
    }

    private void AddPipeLabels(SchematicPipe p, List<(double X, double Y)> path, Layouted lay)
    {
        if (p.Diameter is null && p.Length is null && p.OtherLabels.Count == 0) return;

        // Segmento horizontal mas largo (si no hay, el mas largo).
        int best = -1;
        double bestLen = -1;
        bool horiz = false;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            double len = Math.Abs(path[i + 1].X - path[i].X) + Math.Abs(path[i + 1].Y - path[i].Y);
            bool h = Math.Abs(path[i + 1].Y - path[i].Y) < 1e-6;
            if ((h && !horiz) || (h == horiz && len > bestLen)) { best = i; bestLen = len; horiz = h; }
        }
        if (best < 0) return;
        double mx = (path[best].X + path[best + 1].X) / 2, my = (path[best].Y + path[best + 1].Y) / 2;
        double half = (p.TubeCount - 1) * TubeSpacing / 2;
        string owner = "P" + p.Index;

        if (horiz)
        {
            if (p.Diameter is not null)
            {
                var tb = Block(p.Diameter.Lines.Select(t => new TextLine(t, false)), _c, PipeText, LayPipeText, owner);
                tb.Align = LegendTextHAlign.Center;
                tb.X = mx;
                tb.Top = my + half + 0.8 + tb.Height;
                lay.Texts.Add(tb);
            }
            double top = my - half - 0.8;
            foreach (var l in new[] { p.Length }.Concat(p.OtherLabels).Where(l => l is not null))
            {
                var tb = Block(l!.Lines.Select(t => new TextLine(t, false)), _c, l.IsWarning ? Warn : PipeText, LayPipeText, owner);
                tb.Align = LegendTextHAlign.Center;
                tb.X = mx;
                tb.Top = top;
                top -= tb.Height + 0.4;
                lay.Texts.Add(tb);
            }
        }
        else
        {
            double top = my + _c * 1.2;
            foreach (var l in new[] { p.Diameter, p.Length }.Concat(p.OtherLabels).Where(l => l is not null))
            {
                var tb = Block(l!.Lines.Select(t => new TextLine(t, false)), _c, l.IsWarning ? Warn : PipeText, LayPipeText, owner);
                tb.Align = LegendTextHAlign.Left;
                tb.X = mx + half + 1.0;
                tb.Top = top;
                top -= tb.Height + 0.4;
                lay.Texts.Add(tb);
            }
        }
    }

    private TextBlock EquipmentLabel(SchematicLabel l, SchematicEquipment e, Placed pl)
    {
        // Primera linea en negrita si es el identificador del equipo ([i01] UI8, Facility ID…).
        bool multi = l.Lines.Count > 1;
        var lines = l.Lines.Select((t, i) => new TextLine(t, multi && i == 0)).ToList();
        var tb = Block(lines, _c, l.IsWarning ? Warn : Ink, LayEqText, "E" + e.Index);

        var eb = e.Box;
        var nb = pl.Box;
        double tolS = _d.TextHeight * 0.5;
        static double Gap(double g) => Math.Clamp(g, 1.0, 4.0);

        if (l.Box.MinX >= eb.MaxX - tolS)
        {
            tb.Align = LegendTextHAlign.Left;
            tb.X = nb.MaxX + Gap((l.Box.MinX - eb.MaxX) * _u);
        }
        else if (l.Box.MaxX <= eb.MinX + tolS)
        {
            tb.Align = LegendTextHAlign.Right;
            tb.X = nb.MinX - Gap((eb.MinX - l.Box.MaxX) * _u);
        }
        else
        {
            tb.Align = LegendTextHAlign.Center;
            tb.X = nb.Cx + (l.Box.Cx - eb.Cx) * _u;
        }

        if (l.Box.MinY >= eb.MaxY - tolS)
            tb.Top = nb.MaxY + Gap((l.Box.MinY - eb.MaxY) * _u) + tb.Height;
        else if (l.Box.MaxY <= eb.MinY + tolS)
            tb.Top = nb.MinY - Gap((eb.MinY - l.Box.MaxY) * _u);
        else
            tb.Top = nb.Cy + (l.Box.Cy - eb.Cy) * _u + tb.Height / 2;
        return tb;
    }

    private TextBlock Block(IEnumerable<TextLine> lines, double cap, LegendColor color, string layer, string owner)
    {
        var l = lines.ToList();
        double w = l.Count == 0 ? 0 : l.Max(t => _measurer.MeasureWidth(t.Text, _o.FontFamily, t.Bold, false, cap)) * _o.TextWidthFactor;
        double h = cap + Math.Max(0, l.Count - 1) * cap * 1.6 + cap * 0.35;
        return new TextBlock { Lines = l, Width = w, Height = h, Color = color, Layer = layer, Owner = owner, Cap = cap };
    }

    // ============================================================
    // Solapes
    // ============================================================

    private static HashSet<string> Conflicts(Layouted lay)
    {
        var set = new HashSet<string>();
        var boxes = new List<(string Owner, string Key, SchematicBox Box)>();
        foreach (var kv in lay.Equipment) boxes.Add(("E" + kv.Key, "E" + kv.Key, kv.Value.Box.Inflate(Clearance)));
        for (int i = 0; i < lay.Texts.Count; i++) boxes.Add((lay.Texts[i].Owner, "T" + i, lay.Texts[i].Box.Inflate(Clearance)));

        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                if (boxes[i].Owner != boxes[j].Owner && boxes[i].Box.Intersects(boxes[j].Box))
                    set.Add(boxes[i].Key + "|" + boxes[j].Key);

        // Tuberias que atraviesan cajas ajenas (se toleran sus propios equipos).
        foreach (var kv in lay.Paths)
        {
            var path = kv.Value;
            string own = "P" + kv.Key;
            var ends = new HashSet<string>();
            foreach (var e in lay.Equipment)
                if (e.Value.Box.Contains(path[0].X, path[0].Y, 0.01) || e.Value.Box.Contains(path[^1].X, path[^1].Y, 0.01))
                    ends.Add("E" + e.Key);
            foreach (var b in boxes)
            {
                if (b.Owner == own || ends.Contains(b.Owner)) continue;
                var inner = b.Box.Inflate(-Clearance * 0.5);
                for (int s = 0; s + 1 < path.Count; s++)
                    if (SegmentHitsBox(path[s], path[s + 1], inner)) { set.Add(own + "|" + b.Key); break; }
            }
        }
        return set;
    }

    private static bool SegmentHitsBox((double X, double Y) a, (double X, double Y) b, SchematicBox box)
    {
        // Muestreo suficiente para tramos ortogonales y cajas de rotulo.
        double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X), minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
        if (maxX < box.MinX || minX > box.MaxX || maxY < box.MinY || minY > box.MaxY) return false;
        if (Math.Abs(a.Y - b.Y) < 1e-9) return a.Y > box.MinY && a.Y < box.MaxY && maxX > box.MinX && minX < box.MaxX;
        if (Math.Abs(a.X - b.X) < 1e-9) return a.X > box.MinX && a.X < box.MaxX && maxY > box.MinY && minY < box.MaxY;
        for (int i = 0; i <= 32; i++)
        {
            double t = i / 32.0, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
            if (x > box.MinX && x < box.MaxX && y > box.MinY && y < box.MaxY) return true;
        }
        return false;
    }

    /// <summary>Menor factor k (de 1 hacia abajo) sin solapes que no estuvieran ya en el original.</summary>
    private static double Shrink(Func<double, Layouted> layoutFor, HashSet<string> baseline)
    {
        double best = 1;
        for (double k = 0.98; k >= 0.04; k -= 0.02)
        {
            var c = Conflicts(layoutFor(k));
            if (!c.IsSubsetOf(baseline)) break;
            best = k;
        }
        return best;
    }

    // ============================================================
    // Primitivas (mm de papel)
    // ============================================================

    private abstract record Prim(string Layer, LegendColor Color);
    private sealed record PLine(string Layer, LegendColor Color, double X1, double Y1, double X2, double Y2) : Prim(Layer, Color);
    private sealed record PCircle(string Layer, LegendColor Color, double Cx, double Cy, double R) : Prim(Layer, Color);
    private sealed record PFill(string Layer, LegendColor Color, SchematicBox Box) : Prim(Layer, Color);
    private sealed record PText(string Layer, LegendColor Color, string Text, double X, double Baseline, double Cap, bool Bold, LegendTextHAlign Align) : Prim(Layer, Color);

    private void Emit(Layouted lay, List<Prim> prims)
    {
        // Tuberias: un tubo de liquido y uno o dos de gas, paralelos.
        foreach (var p in _d.Pipes)
        {
            var path = lay.Paths[p.Index];
            int n = p.TubeCount;
            for (int t = 0; t < n; t++)
            {
                double off = (t - (n - 1) / 2.0) * TubeSpacing;
                var line = Offset(path, off);
                bool liquid = t == 0 && n > 1;
                for (int i = 0; i + 1 < line.Count; i++)
                    prims.Add(new PLine(liquid ? LayLiq : LayGas, liquid ? Liquid : Gas, line[i].X, line[i].Y, line[i + 1].X, line[i + 1].Y));
            }
        }

        foreach (var pl in lay.Equipment.Values) EmitSymbol(pl, prims);

        foreach (var tb in lay.Texts)
        {
            double baseline = tb.Top - tb.Cap;
            foreach (var ln in tb.Lines)
            {
                prims.Add(new PText(tb.Layer, tb.Color, ln.Text, tb.X, baseline, tb.Cap, ln.Bold, tb.Align));
                baseline -= tb.Cap * 1.6;
            }
        }
    }

    /// <summary>Polilinea desplazada (inglete) a una distancia con signo.</summary>
    private static List<(double X, double Y)> Offset(List<(double X, double Y)> p, double d)
    {
        if (Math.Abs(d) < 1e-9 || p.Count < 2) return p;
        var normals = new List<(double X, double Y)>();
        for (int i = 0; i + 1 < p.Count; i++)
        {
            double dx = p[i + 1].X - p[i].X, dy = p[i + 1].Y - p[i].Y, len = Math.Sqrt(dx * dx + dy * dy);
            normals.Add(len < 1e-12 ? (0, 0) : (-dy / len, dx / len));
        }
        var r = new List<(double X, double Y)> { (p[0].X + normals[0].X * d, p[0].Y + normals[0].Y * d) };
        for (int i = 1; i + 1 < p.Count; i++)
        {
            var a = normals[i - 1];
            var b = normals[i];
            double denom = 1 + a.X * b.X + a.Y * b.Y;
            if (denom < 1e-6) { r.Add((p[i].X + a.X * d, p[i].Y + a.Y * d)); continue; }
            r.Add((p[i].X + (a.X + b.X) * d / denom, p[i].Y + (a.Y + b.Y) * d / denom));
        }
        r.Add((p[^1].X + normals[^1].X * d, p[^1].Y + normals[^1].Y * d));
        return r;
    }

    private void EmitSymbol(Placed pl, List<Prim> prims)
    {
        var b = pl.Box;
        void Rect(SchematicBox r, LegendColor c)
        {
            prims.Add(new PLine(LayEq, c, r.MinX, r.MinY, r.MaxX, r.MinY));
            prims.Add(new PLine(LayEq, c, r.MaxX, r.MinY, r.MaxX, r.MaxY));
            prims.Add(new PLine(LayEq, c, r.MaxX, r.MaxY, r.MinX, r.MaxY));
            prims.Add(new PLine(LayEq, c, r.MinX, r.MaxY, r.MinX, r.MinY));
        }
        void Centered(string text, double y, double cap, bool bold) =>
            prims.Add(new PText(LayEq, Ink, text, b.Cx, y, cap, bold, LegendTextHAlign.Center));

        // Region rellena gris bajo el simbolo y contorno en verde azulado.
        prims.Add(new PFill(LayEqFill, EquipmentFill, b));
        Rect(b, EquipmentLine);
        switch (pl.Eq.Kind)
        {
            case SchematicEquipmentKind.OutdoorUnit:
            {
                // Dos ventiladores y rotulo «UE».
                double r = Math.Min(b.Width / 4, b.Height / 3) * 0.75;
                double cy = b.MinY + b.Height * 0.6;
                foreach (double fx in new[] { 0.28, 0.72 })
                {
                    double cx = b.MinX + b.Width * fx;
                    prims.Add(new PCircle(LayEq, EquipmentLine, cx, cy, r));
                    prims.Add(new PLine(LayEq, EquipmentLine, cx - r * 0.7, cy, cx + r * 0.7, cy));
                    prims.Add(new PLine(LayEq, EquipmentLine, cx, cy - r * 0.7, cx, cy + r * 0.7));
                }
                Centered("UE", b.MinY + b.Height * 0.12, _ct, true);
                break;
            }
            case SchematicEquipmentKind.HeatRecovery:
                Centered("CR", b.Cy - _ct * 0.5, _ct, true);
                break;
            case SchematicEquipmentKind.RiserHole:
                prims.Add(new PLine(LayEq, EquipmentLine, b.MinX, b.MinY, b.MaxX, b.MaxY));
                prims.Add(new PLine(LayEq, EquipmentLine, b.MinX, b.MaxY, b.MaxX, b.MinY));
                break;
            case SchematicEquipmentKind.Branch:
                prims.Add(new PLine(LayEq, EquipmentLine, b.MinX, b.Cy, b.MaxX, b.MaxY));
                prims.Add(new PLine(LayEq, EquipmentLine, b.MinX, b.Cy, b.MaxX, b.MinY));
                break;
            case SchematicEquipmentKind.IndoorUnit:
            {
                double r = Math.Min(b.Height * 0.3, 3.0);
                double cx = b.MaxX - b.Height * 0.55;
                prims.Add(new PCircle(LayEq, EquipmentLine, cx, b.Cy, r));
                prims.Add(new PLine(LayEq, EquipmentLine, b.MinX + b.Width * 0.12, b.MinY, b.MinX + b.Width * 0.12, b.MaxY));
                break;
            }
            case SchematicEquipmentKind.OutdoorConnector:
            {
                double y = b.MaxY - _c * 1.6;
                foreach (var a in pl.Eq.Attributes)
                {
                    Centered(a, y, _c, false);
                    y -= _c * 1.6;
                }
                break;
            }
        }
    }

    private void EmitTitleBlock(List<Prim> prims)
    {
        var b = Bounds(prims);
        double x = b.MinX, y = b.MinY - 12;

        string? facility = _d.Equipment.SelectMany(e => e.Labels).SelectMany(l => l.Lines)
            .FirstOrDefault(t => t.StartsWith("Facility ID", StringComparison.OrdinalIgnoreCase));
        string sub = (facility is not null ? "Sistema " + facility.Split(':').Last().Trim() + "  ·  " : string.Empty) +
                     $"Origen: {_d.ProfileName} ({System.IO.Path.GetFileName(_d.FilePath)})";

        prims.Add(new PText(LayTitle, Ink, _o.Title, x, y, _ct, true, LegendTextHAlign.Left));
        y -= _ct * 0.5 + _c * 1.6;
        prims.Add(new PText(LayTitle, Aux, sub, x, y, _c, false, LegendTextHAlign.Left));
        y -= _c * 3.2;

        void LegendRow(LegendColor color, string layer, string text)
        {
            prims.Add(new PLine(layer, color, x, y + _c * 0.5, x + 15, y + _c * 0.5));
            prims.Add(new PText(LayTitle, Ink, text, x + 18, y, _c, false, LegendTextHAlign.Left));
            y -= _c * 2.2;
        }
        LegendRow(Liquid, LayLiq, $"Línea de líquido refrigerante  ({LiquidStyle})");
        LegendRow(Gas, LayGas, $"Línea de gas refrigerante  ({GasStyle})");
        prims.Add(new PText(LayTitle, Aux,
            "Diámetros en pulgadas: líquido : gas [: gas]  ·  Longitudes: tramo / acumulada desde la unidad exterior",
            x, y, _c, false, LegendTextHAlign.Left));
    }

    private SchematicBox Bounds(IEnumerable<Prim> prims)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Add(double x, double y) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        foreach (var p in prims)
            switch (p)
            {
                case PLine l: Add(l.X1, l.Y1); Add(l.X2, l.Y2); break;
                case PCircle c: Add(c.Cx - c.R, c.Cy - c.R); Add(c.Cx + c.R, c.Cy + c.R); break;
                case PFill fl: Add(fl.Box.MinX, fl.Box.MinY); Add(fl.Box.MaxX, fl.Box.MaxY); break;
                case PText t:
                    double w = _measurer.MeasureWidth(t.Text, _o.FontFamily, t.Bold, false, t.Cap) * _o.TextWidthFactor;
                    double x0 = t.Align switch { LegendTextHAlign.Center => t.X - w / 2, LegendTextHAlign.Right => t.X - w, _ => t.X };
                    Add(x0, t.Baseline - t.Cap * 0.35); Add(x0 + w, t.Baseline + t.Cap);
                    break;
            }
        return minX > maxX ? default : new SchematicBox(minX, minY, maxX, maxY);
    }

    /// <summary>Pasa a <see cref="LegendDrawing"/> en mm reales: origen en la esquina superior izquierda, × g × escala.</summary>
    private LegendDrawing Build(List<Prim> prims, SchematicBox b, double g)
    {
        double f = g * _o.Scale;
        double X(double x) => (x - b.MinX) * f;
        double Y(double y) => (y - b.MaxY) * f;

        var d = new LegendDrawing { FilePath = _d.FilePath, SuggestedUnits = LegendUnits.Millimeters, DeclaredUnits = LegendUnits.Millimeters };
        foreach (var p in prims)
        {
            switch (p)
            {
                case PLine l:
                    d.Items.Add(new LegendCurve(l.Layer, l.Color, null)
                    {
                        Kind = LegendCurveKind.Line, X1 = X(l.X1), Y1 = Y(l.Y1), X2 = X(l.X2), Y2 = Y(l.Y2),
                    });
                    break;
                case PFill fl:
                {
                    double x0 = X(fl.Box.MinX), y0 = Y(fl.Box.MinY), x1 = X(fl.Box.MaxX), y1 = Y(fl.Box.MaxY);
                    var loop = new List<LegendSegment>
                    {
                        LegendSegment.Line(x0, y0, x1, y0), LegendSegment.Line(x1, y0, x1, y1),
                        LegendSegment.Line(x1, y1, x0, y1), LegendSegment.Line(x0, y1, x0, y0),
                    };
                    d.Items.Add(new LegendFill(fl.Layer, fl.Color, new[] { loop }));
                    break;
                }
                case PCircle c:
                    d.Items.Add(new LegendCurve(c.Layer, c.Color, null)
                    {
                        Kind = LegendCurveKind.Arc, Cx = X(c.Cx), Cy = Y(c.Cy), R = c.R * f, A0 = 0, A1 = 2 * Math.PI,
                    });
                    break;
                case PText t:
                {
                    double h = t.Cap * f;
                    bool title = Math.Abs(t.Cap - _ct) < 1e-9 && Math.Abs(_ct - _c) > 1e-9;
                    string key = title ? SchematicTextStyles.Title : t.Bold ? SchematicTextStyles.Bold : SchematicTextStyles.Regular;
                    bool bold = title ? _o.TitleBold : t.Bold && _o.BoldAvailable;
                    d.Items.Add(new LegendText(t.Layer, t.Color)
                    {
                        Text = t.Text, X = X(t.X), BaselineY = Y(t.Baseline), Height = h,
                        FontFamily = _o.FontFamily, Bold = bold, HAlign = t.Align, StyleKey = key,
                        WidthFactor = _o.TextWidthFactor,
                        MeasuredWidth = _measurer.MeasureWidth(t.Text, _o.FontFamily, bold, false, h) * _o.TextWidthFactor,
                    });
                }
                    break;
            }
        }

        d.MinX = 0;
        d.MaxX = b.Width * f;
        d.MinY = -b.Height * f;
        d.MaxY = 0;
        foreach (var grp in d.Items.GroupBy(i => i.Layer))
            d.Layers.Add(new LegendLayerInfo { Name = grp.Key, Color = grp.First().Color, ItemCount = grp.Count() });
        return d;
    }
}
