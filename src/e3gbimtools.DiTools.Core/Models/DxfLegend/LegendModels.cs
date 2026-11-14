using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.DxfLegend;

/// <summary>
/// Unidades de dibujo del DXF.  El campo $INSUNITS no es fiable (las plantillas
/// de leyenda E3G declaran milimetros pero estan dibujadas en metros), por eso
/// la ventana deja elegirlas y propone una a partir del tamaño del dibujo.
/// </summary>
public enum LegendUnits
{
    Meters,
    Centimeters,
    Millimeters,
    Inches,
    Feet,
}

public static class LegendUnitsExtensions
{
    /// <summary>Pies de Revit por cada unidad de dibujo.</summary>
    public static double ToFeet(this LegendUnits u) => u switch
    {
        LegendUnits.Meters      => 1.0 / 0.3048,
        LegendUnits.Centimeters => 0.01 / 0.3048,
        LegendUnits.Millimeters => 0.001 / 0.3048,
        LegendUnits.Inches      => 1.0 / 12.0,
        LegendUnits.Feet        => 1.0,
        _ => throw new ArgumentOutOfRangeException(nameof(u)),
    };

    /// <summary>Milimetros reales por cada unidad de dibujo.</summary>
    public static double ToMm(this LegendUnits u) => u.ToFeet() * 304.8;

    public static string Label(this LegendUnits u) => u switch
    {
        LegendUnits.Meters      => "Metros",
        LegendUnits.Centimeters => "Centímetros",
        LegendUnits.Millimeters => "Milímetros",
        LegendUnits.Inches      => "Pulgadas",
        LegendUnits.Feet        => "Pies",
        _ => u.ToString(),
    };
}

/// <summary>Color RGB ya resuelto (ByLayer / ByBlock aplicados, blanco ACI 7 → negro).</summary>
public readonly record struct LegendColor(byte R, byte G, byte B)
{
    public static readonly LegendColor Black = new(0, 0, 0);
    public string Key => $"{R}-{G}-{B}";
    public bool IsBlack => R == 0 && G == 0 && B == 0;
}

/// <summary>
/// Patron de linea del DXF ya escalado a unidades de dibujo (LTSCALE × escala
/// de la entidad).  Segmentos: &gt;0 trazo, &lt;0 hueco, 0 punto.
/// </summary>
public sealed class LegendLinePattern
{
    public LegendLinePattern(string name, IReadOnlyList<double> segments)
    {
        Name = name;
        Segments = segments;
    }

    public string Name { get; }
    public IReadOnlyList<double> Segments { get; }

    /// <summary>Clave estable (nombre + longitudes) para cachear el LinePatternElement de Revit.</summary>
    public string Key => Name + "|" + string.Join(";", Segments.Select(s => s.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)));
}

public enum LegendSegmentKind { Line, Arc }

/// <summary>
/// Tramo de un contorno de relleno.  Los arcos son siempre antihorarios de
/// <see cref="A0"/> a <see cref="A1"/> (radianes, A1 &gt; A0); el sentido real del
/// contorno lo reconstruye el generador encadenando extremos.
/// </summary>
public readonly record struct LegendSegment(
    LegendSegmentKind Kind,
    double X1, double Y1, double X2, double Y2,
    double Cx = 0, double Cy = 0, double R = 0, double A0 = 0, double A1 = 0)
{
    public static LegendSegment Line(double x1, double y1, double x2, double y2) =>
        new(LegendSegmentKind.Line, x1, y1, x2, y2);

    public static LegendSegment Arc(double cx, double cy, double r, double a0, double a1) =>
        new(LegendSegmentKind.Arc,
            cx + r * Math.Cos(a0), cy + r * Math.Sin(a0),
            cx + r * Math.Cos(a1), cy + r * Math.Sin(a1),
            cx, cy, r, a0, a1);
}

public abstract class LegendItem
{
    protected LegendItem(string layer, LegendColor color)
    {
        Layer = layer;
        Color = color;
    }

    /// <summary>Capa efectiva (las entidades de la capa 0 de un bloque toman la del bloque).</summary>
    public string Layer { get; }
    public LegendColor Color { get; }
}

public enum LegendCurveKind { Line, Arc, Ellipse }

/// <summary>Linea, arco (antihorario A0→A1) o elipse en unidades de dibujo.</summary>
public sealed class LegendCurve : LegendItem
{
    public LegendCurve(string layer, LegendColor color, LegendLinePattern? pattern) : base(layer, color)
    {
        Pattern = pattern;
    }

    public LegendCurveKind Kind { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }

    public double Cx { get; init; }
    public double Cy { get; init; }
    /// <summary>Radio (arco) o semieje mayor (elipse).</summary>
    public double R { get; init; }
    /// <summary>Semieje menor (solo elipse).</summary>
    public double R2 { get; init; }
    /// <summary>Giro del eje mayor (solo elipse), radianes.</summary>
    public double Rotation { get; init; }
    public double A0 { get; init; }
    public double A1 { get; init; }

    /// <summary>Null = continua.</summary>
    public LegendLinePattern? Pattern { get; }
}

/// <summary>Relleno solido (hatch SOLID, polilinea con grosor, SOLID 2D, flecha de directriz).</summary>
public class LegendFill : LegendItem
{
    public LegendFill(string layer, LegendColor color, IReadOnlyList<IReadOnlyList<LegendSegment>> loops)
        : base(layer, color)
    {
        Loops = loops;
    }

    public IReadOnlyList<IReadOnlyList<LegendSegment>> Loops { get; }
}

public enum LegendTextHAlign { Left, Center, Right }

/// <summary>
/// Una linea de texto ya maquetada: el MTEXT se parte en lineas (con su sangria
/// y su ajuste de ancho) para respetar la interlinea de AutoCAD, que Revit no
/// permite controlar.  (X, BaselineY) es el punto de la linea base segun
/// <see cref="HAlign"/>.
/// </summary>
public sealed class LegendText : LegendItem
{
    public LegendText(string layer, LegendColor color) : base(layer, color) { }

    public string Text { get; init; } = string.Empty;
    public double X { get; init; }
    public double BaselineY { get; init; }
    /// <summary>Altura del texto (altura de mayusculas) en unidades de dibujo.</summary>
    public double Height { get; init; }
    public string FontFamily { get; init; } = "Arial";
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public double WidthFactor { get; init; } = 1.0;
    /// <summary>Radianes, antihorario.</summary>
    public double Rotation { get; init; }
    public LegendTextHAlign HAlign { get; init; }
    /// <summary>Ancho medido de la linea en unidades de dibujo (para dimensionar el TextNote).</summary>
    public double MeasuredWidth { get; init; }
    /// <summary>
    /// Estilo logico (esquema de principio).  Si el request trae un tipo de texto
    /// para esta clave se usa ese tipo existente y no se crea ninguno.
    /// </summary>
    public string? StyleKey { get; init; }
}

/// <summary>Resumen de una capa del DXF para la lista de la ventana.</summary>
public sealed class LegendLayerInfo
{
    public string Name { get; init; } = string.Empty;
    public LegendColor Color { get; init; }
    public int ItemCount { get; set; }
    /// <summary>Capa apagada/inutilizada en el DXF, o marco de ventana grafica (Defpoints…).</summary>
    public bool ExcludedByDefault { get; init; }
}

/// <summary>Resultado de leer y aplanar un DXF/DWG.</summary>
public sealed class LegendDrawing
{
    public string FilePath { get; init; } = string.Empty;
    public List<LegendItem> Items { get; } = new();
    public List<LegendLayerInfo> Layers { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>Unidades segun $INSUNITS (null si no declaradas).</summary>
    public LegendUnits? DeclaredUnits { get; init; }
    /// <summary>Unidades propuestas a partir del tamaño del dibujo.</summary>
    public LegendUnits SuggestedUnits { get; set; }

    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    public int Count<T>() where T : LegendItem => Items.OfType<T>().Count();

    /// <summary>
    /// Entidad de primer nivel del espacio modelo (objeto ACadSharp) y el rango de
    /// <see cref="Items"/> que ha generado.  Vacio en dibujos compuestos por codigo.
    /// </summary>
    public List<LegendSourceRange> Sources { get; } = new();
}

/// <summary>Entidad de origen (ACadSharp) → elementos <c>Items[Start .. Start+Count)</c>.</summary>
public sealed record LegendSourceRange(object Entity, int Start, int Count);

/// <summary>Opciones de generacion en Revit.</summary>
public sealed class DxfLegendRequest
{
    public ElementId ViewId { get; init; } = ElementId.InvalidElementId;
    public LegendUnits Units { get; init; } = LegendUnits.Meters;
    /// <summary>Escala de la vista destino (1:N).  Si difiere de la actual se cambia.</summary>
    public int Scale { get; init; } = 100;
    public ISet<string> ExcludedLayers { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Prefijo de los estilos de linea, tipos de relleno, patrones y tipos de texto creados.</summary>
    public string StylePrefix { get; init; } = "E3G_LEY";
    /// <summary>Factor de ancho extra de los textos (Calibri sale ~2 % mas ancha en Revit que en AutoCAD).</summary>
    public double TextWidthFactor { get; init; } = 0.98;
    /// <summary>Por debajo de este grosor en papel (mm) una polilinea con grosor se dibuja como linea, no como relleno.</summary>
    public double ThinBandPaperMm { get; init; } = 0.2;
    /// <summary>Borra lineas de detalle, regiones y textos existentes en la vista antes de generar.</summary>
    public bool ClearView { get; init; }
    /// <summary>Lleva la esquina superior izquierda del dibujo al origen de la vista.</summary>
    public bool MoveToOrigin { get; init; }
    /// <summary>
    /// Nombres de estilo de linea por color (<see cref="LegendColor.Key"/>), sin prefijo.
    /// Sin entrada se usa «Prefijo_R-G-B».  Lo usa el esquema de principio (50_12_…, 50_13_…).
    /// </summary>
    public IReadOnlyDictionary<string, string>? LineStyleNames { get; init; }
    /// <summary>Grosor de linea (1-16) por nombre de estilo; por defecto 1.</summary>
    public IReadOnlyDictionary<string, int>? LineWeights { get; init; }
    /// <summary>Tipos de texto EXISTENTES por <see cref="LegendText.StyleKey"/> (no se crean ni modifican).</summary>
    public IReadOnlyDictionary<string, ElementId>? TextTypes { get; init; }
}

public sealed class DxfLegendResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int Curves { get; init; }
    public int Fills { get; init; }
    public int Texts { get; init; }
    public int Deleted { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public long ElapsedMs { get; init; }

    public string ToSummary() =>
        $"{Curves} líneas/arcos, {Fills} rellenos y {Texts} textos creados" +
        (Deleted > 0 ? $" ({Deleted} elementos previos borrados)" : string.Empty) +
        $" en {ElapsedMs / 1000.0:0.0} s.";
}

/// <summary>Tipo de texto del proyecto (para elegir uno existente).</summary>
public sealed class LegendTextTypeInfo
{
    public ElementId Id { get; init; } = ElementId.InvalidElementId;
    public string Name { get; init; } = string.Empty;
    public string Font { get; init; } = string.Empty;
    public double SizeMm { get; init; }
    public bool Bold { get; init; }
    public double WidthFactor { get; init; } = 1.0;
    public string DisplayName => $"{Name}   ·   {Font} {SizeMm.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-ES"))} mm{(Bold ? " negrita" : "")}";
}

/// <summary>Vista candidata (leyenda o dibujo) para la lista de destino.</summary>
public sealed class LegendTargetView
{
    public ElementId Id { get; init; } = ElementId.InvalidElementId;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public int Scale { get; init; }
    public bool IsActive { get; init; }
    public string DisplayName => $"{Name}   ·   {Kind}  1:{Scale}";
}
