using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using E3G.BimTools.DiTools.Core.Models.DialuxImport;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Services.DialuxImport;

/// <summary>
/// Lee un DWG/DXF exportado de DIALux (ACadSharp) y saca todas las inserciones
/// de bloque con su posicion, giro, capa y atributos.  No decide cuales son
/// luminarias: eso lo hace el mapeo bloque → tipo de Revit.
///
///   • Recorre tambien los bloques anidados componiendo las transformaciones.
///   • Capa 0 dentro de un bloque → capa del INSERT que lo contiene.
///   • DIALux exporta cada luminaria como un bloque ANONIMO propio (*U12) en la
///     capa DLXLUM, sin atributos, con el simbolo ya girado dentro y un unico
///     texto (capa DLXLUMEX) con el numero de luminaria del proyecto.  Esas
///     inserciones se agrupan por ese numero ("DIALux 1 · descripcion", la
///     descripcion sale de la leyenda de la capa DLXDESCR) y su giro se toma de
///     la linea mas larga del simbolo.  El resto de bloques anonimos (cotas,
///     tablas, leyendas…) se ignoran.
/// No necesita contexto de Revit.
/// </summary>
public sealed class DialuxDwgReader
{
    private const int MaxDepth = 8;
    public const string DialuxLuminaireLayer = "DLXLUM";
    private const string DialuxLegendLayer = "DLXDESCR";

    private DialuxDrawing _out = null!;
    private Dictionary<string, string> _legend = new();
    private readonly Dictionary<string, IReadOnlyList<DialuxSegment>> _symbols = new(StringComparer.OrdinalIgnoreCase);

    public DialuxDrawing Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el archivo.", path);

        CadDocument doc = Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase)
            ? DwgReader.Read(path)
            : DxfReader.Read(path);

        _out = new DialuxDrawing
        {
            FilePath = path,
            DeclaredUnits = MapUnits(doc.Header.InsUnits),
        };

        _legend = ReadLegend(doc);

        foreach (var e in doc.Entities)
            if (e is Insert ins)
                Visit(ins, Frame.Identity, null, 0);

        BuildSummary();
        _out.SuggestedUnits = _out.DeclaredUnits ?? GuessUnits();
        if (_out.Inserts.Count == 0)
            _out.Warnings.Add("El archivo no tiene bloques insertados en espacio modelo: DIALux debe exportar las luminarias como bloques.");
        return _out;
    }

    private void Visit(Insert ins, Frame parent, string? parentLayer, int depth)
    {
        if (ins.IsInvisible || depth > MaxDepth || ins.Block is null) return;

        string blockName = ins.Block.Name ?? string.Empty;
        string layer = ins.Layer?.Name ?? "0";
        if (layer == "0" && parentLayer is not null) layer = parentLayer;

        // Bloque anonimo: solo interesa si es una luminaria de DIALux.
        double symbolRotation = 0;
        bool isLuminaire = false;
        var attributes = ReadAttributes(ins);
        if (blockName.StartsWith("*", StringComparison.Ordinal))
        {
            if (!TryDialuxLuminaire(ins.Block, layer, out var number, out symbolRotation)) return;
            isLuminaire = true;
            _legend.TryGetValue(number, out var description);
            blockName = string.IsNullOrEmpty(description) ? $"DIALux {number}" : $"DIALux {number} · {description}";
            attributes = new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase)
            {
                ["DIALux_N"] = number,
                ["DIALux_Desc"] = description ?? string.Empty,
            };
        }

        if (!_symbols.ContainsKey(blockName))
            _symbols[blockName] = ReadSymbol(ins.Block);

        var basePt = ins.Block.BlockEntity?.BasePoint ?? CSMath.XYZ.Zero;
        int rows = Math.Max(1, (int)ins.RowCount), cols = Math.Max(1, (int)ins.ColumnCount);

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var frame = parent.Compose(Frame.FromInsert(ins, basePt, c * ins.ColumnSpacing, r * ins.RowSpacing));
                // El punto de insercion de este INSERT es la imagen del punto base del bloque.
                var (x, y) = frame.Apply(basePt.X, basePt.Y);

                _out.Inserts.Add(new DialuxInsert
                {
                    Handle = rows * cols == 1 ? ins.Handle.ToString("X") : $"{ins.Handle:X}-{r}-{c}",
                    BlockName = blockName,
                    Layer = layer,
                    X = x,
                    Y = y,
                    Z = frame.Z(basePt.Z),
                    Rotation = isLuminaire ? DirectionAngle(frame, symbolRotation) : frame.Rotation,
                    Mirrored = frame.Determinant < 0,
                    Depth = depth,
                    IsDialuxLuminaire = isLuminaire,
                    Attributes = attributes,
                });

                foreach (var child in ins.Block.Entities)
                    if (child is Insert nested)
                        Visit(nested, frame, layer, depth + 1);
            }
    }

    /// <summary>
    /// Luminaria de DIALux: bloque anonimo con un unico texto corto (el numero de
    /// luminaria).  En la capa DLXLUM basta con eso; fuera de ella se exige
    /// ademas que el texto sea un numero, para no confundirla con otros simbolos.
    /// El giro del simbolo es el de su linea mas larga, orientada desde el punto base.
    /// </summary>
    private static bool TryDialuxLuminaire(BlockRecord block, string layer, out string number, out double rotation)
    {
        number = string.Empty;
        rotation = 0;

        var texts = block.Entities.OfType<TextEntity>().Select(t => t.Value)
            .Concat(block.Entities.OfType<MText>().Select(t => t.Value))
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0)
            .ToList();
        if (texts.Count != 1 || texts[0].Length > 12) return false;

        bool onLuminaireLayer = string.Equals(layer, DialuxLuminaireLayer, StringComparison.OrdinalIgnoreCase);
        if (!onLuminaireLayer && !texts[0].All(char.IsDigit)) return false;
        number = texts[0];

        var basePt = block.BlockEntity?.BasePoint ?? CSMath.XYZ.Zero;
        var longest = block.Entities.OfType<Line>()
            .OrderByDescending(l => Len2(l.EndPoint.X - l.StartPoint.X, l.EndPoint.Y - l.StartPoint.Y))
            .FirstOrDefault();
        if (longest is not null)
        {
            // Desde el extremo mas cercano al punto base hacia el mas lejano.
            var (near, far) = Len2(longest.StartPoint.X - basePt.X, longest.StartPoint.Y - basePt.Y)
                              <= Len2(longest.EndPoint.X - basePt.X, longest.EndPoint.Y - basePt.Y)
                ? (longest.StartPoint, longest.EndPoint)
                : (longest.EndPoint, longest.StartPoint);
            rotation = Math.Atan2(far.Y - near.Y, far.X - near.X);
        }
        return true;
    }

    private static double Len2(double dx, double dy) => dx * dx + dy * dy;

    private const int MaxSymbolSegments = 400;

    /// <summary>
    /// Trazos del simbolo en ejes del bloque (lineas, polilineas, circulos y
    /// arcos; un nivel de bloques anidados) para la miniatura de la tabla.
    /// </summary>
    private static IReadOnlyList<DialuxSegment> ReadSymbol(BlockRecord block)
    {
        var segs = new List<DialuxSegment>();
        AddSymbol(block, Frame.Identity, segs, 0);
        return segs;
    }

    private static void AddSymbol(BlockRecord block, Frame f, List<DialuxSegment> segs, int depth)
    {
        void Seg(double x1, double y1, double x2, double y2)
        {
            if (segs.Count >= MaxSymbolSegments) return;
            var (a, b) = f.Apply(x1, y1);
            var (c, d) = f.Apply(x2, y2);
            segs.Add(new DialuxSegment(a, b, c, d));
        }
        void ArcSegs(double cx, double cy, double r, double a0, double a1)
        {
            if (a1 <= a0) a1 += 2 * Math.PI;
            int n = Math.Max(4, (int)Math.Ceiling((a1 - a0) / (Math.PI / 12)));
            for (int k = 0; k < n; k++)
            {
                double t0 = a0 + (a1 - a0) * k / n, t1 = a0 + (a1 - a0) * (k + 1) / n;
                Seg(cx + r * Math.Cos(t0), cy + r * Math.Sin(t0), cx + r * Math.Cos(t1), cy + r * Math.Sin(t1));
            }
        }

        foreach (var e in block.Entities)
        {
            if (e.IsInvisible) continue;
            switch (e)
            {
                case Line l:
                    Seg(l.StartPoint.X, l.StartPoint.Y, l.EndPoint.X, l.EndPoint.Y);
                    break;
                case LwPolyline p:
                    {
                        var v = p.Vertices;
                        for (int k = 0; k + 1 < v.Count; k++) Seg(v[k].Location.X, v[k].Location.Y, v[k + 1].Location.X, v[k + 1].Location.Y);
                        if (p.IsClosed && v.Count > 2) Seg(v[^1].Location.X, v[^1].Location.Y, v[0].Location.X, v[0].Location.Y);
                        break;
                    }
                case Arc a:
                    ArcSegs(a.Center.X, a.Center.Y, a.Radius, a.StartAngle, a.EndAngle);
                    break;
                case Circle c:
                    ArcSegs(c.Center.X, c.Center.Y, c.Radius, 0, 2 * Math.PI);
                    break;
                case Insert nested when depth < 1 && nested.Block is not null:
                    {
                        var bp = nested.Block.BlockEntity?.BasePoint ?? CSMath.XYZ.Zero;
                        AddSymbol(nested.Block, f.Compose(Frame.FromInsert(nested, bp, 0, 0)), segs, depth + 1);
                        break;
                    }
            }
        }
    }

    /// <summary>Angulo en el espacio modelo de una direccion dada en ejes del bloque.</summary>
    private static double DirectionAngle(Frame f, double localAngle)
    {
        double x = Math.Cos(localAngle), y = Math.Sin(localAngle);
        return Math.Atan2(f.C * x + f.D * y, f.A * x + f.B * y);
    }

    /// <summary>
    /// Leyenda de luminarias de DIALux (bloque de la capa DLXDESCR): textos en
    /// orden "… | 1 | FABRICANTE MODELO … | 2 | …".  Devuelve numero → descripcion.
    /// </summary>
    private static Dictionary<string, string> ReadLegend(CadDocument doc)
    {
        var legend = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ins in doc.Entities.OfType<Insert>()
                     .Where(i => string.Equals(i.Layer?.Name, DialuxLegendLayer, StringComparison.OrdinalIgnoreCase)))
        {
            if (ins.Block is null) continue;
            var texts = ins.Block.Entities.OfType<TextEntity>().Select(t => t.Value)
                .Concat(ins.Block.Entities.OfType<MText>().Select(t => t.Value))
                .Select(t => (t ?? string.Empty).Trim())
                .Where(t => t.Length > 0)
                .ToList();
            for (int k = 0; k + 1 < texts.Count; k++)
                if (texts[k].Length <= 4 && texts[k].All(char.IsDigit) && !texts[k + 1].All(c => char.IsDigit(c) || c == '.' || c == ','))
                    legend.TryAdd(texts[k], texts[k + 1]);
        }
        return legend;
    }

    private static IReadOnlyDictionary<string, string> ReadAttributes(Insert ins)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in ins.Attributes)
        {
            if (string.IsNullOrWhiteSpace(a.Tag)) continue;
            d[a.Tag] = a.Value ?? string.Empty;
        }
        return d;
    }

    private void BuildSummary()
    {
        foreach (var g in _out.Inserts.GroupBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.First().IsDialuxLuminaire)
                     .ThenByDescending(g => g.Min(i => i.Depth) == 0)
                     .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var first = g.First();
            _out.Blocks.Add(new DialuxBlockInfo
            {
                Name = g.Key,
                Count = g.Count(),
                Layers = g.Select(i => i.Layer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(l => l).ToList(),
                AttributesSample = string.Join("; ", first.Attributes.Select(kv => $"{kv.Key}={kv.Value}")),
                AnyNested = g.Any(i => i.Depth > 0),
                IsDialuxLuminaire = first.IsDialuxLuminaire,
                Symbol = _symbols.TryGetValue(g.Key, out var sym) ? sym : Array.Empty<DialuxSegment>(),
            });
        }
        _out.Layers.AddRange(_out.Inserts.Select(i => i.Layer).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase));
    }

    /// <summary>Unidades por la extension de las inserciones: un edificio mide decenas de metros.</summary>
    private LegendUnits GuessUnits()
    {
        if (_out.Inserts.Count < 2) return LegendUnits.Meters;
        double w = _out.Inserts.Max(i => i.X) - _out.Inserts.Min(i => i.X);
        double h = _out.Inserts.Max(i => i.Y) - _out.Inserts.Min(i => i.Y);
        double size = Math.Max(w, h);
        return size < 1500 ? LegendUnits.Meters : size < 15000 ? LegendUnits.Centimeters : LegendUnits.Millimeters;
    }

    private static LegendUnits? MapUnits(ACadSharp.Types.Units.UnitsType u) => u switch
    {
        ACadSharp.Types.Units.UnitsType.Meters      => LegendUnits.Meters,
        ACadSharp.Types.Units.UnitsType.Centimeters => LegendUnits.Centimeters,
        ACadSharp.Types.Units.UnitsType.Millimeters => LegendUnits.Millimeters,
        ACadSharp.Types.Units.UnitsType.Inches      => LegendUnits.Inches,
        ACadSharp.Types.Units.UnitsType.Feet        => LegendUnits.Feet,
        _ => null,
    };

    /// <summary>Transformacion afin en planta + cota: x' = A·x + t, z' = sz·z + tz.</summary>
    internal readonly record struct Frame(double A, double B, double C, double D, double Tx, double Ty, double Sz, double Tz)
    {
        public static readonly Frame Identity = new(1, 0, 0, 1, 0, 0, 1, 0);

        public (double X, double Y) Apply(double x, double y) => (A * x + B * y + Tx, C * x + D * y + Ty);
        public double Z(double z) => Sz * z + Tz;
        public double Determinant => A * D - B * C;
        /// <summary>Giro del eje X local del bloque, radianes.</summary>
        public double Rotation => Math.Atan2(C, A);

        /// <summary>this ∘ inner (primero inner, luego this).</summary>
        public Frame Compose(Frame inner) => new(
            A * inner.A + B * inner.C, A * inner.B + B * inner.D,
            C * inner.A + D * inner.C, C * inner.B + D * inner.D,
            A * inner.Tx + B * inner.Ty + Tx, C * inner.Tx + D * inner.Ty + Ty,
            Sz * inner.Sz, Sz * inner.Tz + Tz);

        /// <summary>x' = P + R(θ)·S·(x − base + desplazamiento MINSERT).</summary>
        public static Frame FromInsert(Insert ins, CSMath.XYZ basePt, double colOffset, double rowOffset)
        {
            double sx = ins.XScale == 0 ? 1 : ins.XScale, sy = ins.YScale == 0 ? 1 : ins.YScale;
            double sz = ins.ZScale == 0 ? 1 : ins.ZScale;
            double cos = Math.Cos(ins.Rotation), sin = Math.Sin(ins.Rotation);
            double a = cos * sx, b = -sin * sy, c = sin * sx, d = cos * sy;
            double ox = cos * colOffset - sin * rowOffset, oy = sin * colOffset + cos * rowOffset;
            double tx = ins.InsertPoint.X + ox - (a * basePt.X + b * basePt.Y);
            double ty = ins.InsertPoint.Y + oy - (c * basePt.X + d * basePt.Y);
            var f = new Frame(a, b, c, d, tx, ty, sz, ins.InsertPoint.Z - sz * basePt.Z);
            // Extrusion (0,0,-1): el bloque se ve en espejo respecto al eje Y del SCU.
            return ins.Normal.Z < 0 ? new Frame(-1, 0, 0, 1, 0, 0, 1, 0).Compose(f) : f;
        }
    }
}
