using System;
using System.Collections.Generic;
using System.Linq;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Models.Schematic;

// =====================================================================
// Esquema de principio de climatizacion (2026-11-12).
//
// Un esquema exportado por el software del fabricante (LG LATS, Daikin,
// Mitsubishi…) se interpreta como un grafo: equipos (bloques) unidos por
// tramos de tuberia, con sus rotulos.  Con ese modelo se redibuja con estilo
// E3G y se compacta para que quepa en el plano sin perder informacion.
// Coordenadas "src" = unidades del DXF.
// =====================================================================

public enum SchematicEquipmentKind
{
    OutdoorUnit,
    OutdoorConnector,
    HeatRecovery,
    Branch,
    RiserHole,
    IndoorUnit,
    Other,
}

public static class SchematicEquipmentKindExtensions
{
    public static string Label(this SchematicEquipmentKind k) => k switch
    {
        SchematicEquipmentKind.OutdoorUnit => "Unidad exterior",
        SchematicEquipmentKind.OutdoorConnector => "Conector de unidades exteriores",
        SchematicEquipmentKind.HeatRecovery => "Caja de recuperación",
        SchematicEquipmentKind.Branch => "Derivación",
        SchematicEquipmentKind.RiserHole => "Derivación en montante",
        SchematicEquipmentKind.IndoorUnit => "Unidad interior",
        _ => "Otro",
    };
}

/// <summary>Caja en coordenadas de origen.</summary>
public readonly record struct SchematicBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double Cx => (MinX + MaxX) / 2;
    public double Cy => (MinY + MaxY) / 2;

    public bool Contains(double x, double y, double tol) =>
        x >= MinX - tol && x <= MaxX + tol && y >= MinY - tol && y <= MaxY + tol;

    /// <summary>Distancia de un punto a la caja (0 dentro).</summary>
    public double Distance(double x, double y)
    {
        double dx = Math.Max(Math.Max(MinX - x, 0), x - MaxX);
        double dy = Math.Max(Math.Max(MinY - y, 0), y - MaxY);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Distancia entre cajas (0 si se tocan o solapan).</summary>
    public double Distance(SchematicBox o)
    {
        double dx = Math.Max(0, Math.Max(o.MinX - MaxX, MinX - o.MaxX));
        double dy = Math.Max(0, Math.Max(o.MinY - MaxY, MinY - o.MaxY));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public SchematicBox Inflate(double d) => new(MinX - d, MinY - d, MaxX + d, MaxY + d);

    public bool Intersects(SchematicBox o) =>
        MinX < o.MaxX && o.MinX < MaxX && MinY < o.MaxY && o.MinY < MaxY;

    public static SchematicBox Union(IEnumerable<SchematicBox> boxes)
    {
        var l = boxes.ToList();
        return l.Count == 0 ? default : new(l.Min(b => b.MinX), l.Min(b => b.MinY), l.Max(b => b.MaxX), l.Max(b => b.MaxY));
    }
}

/// <summary>Rotulo (MTEXT/TEXT) del esquema, ya sin formato.</summary>
public sealed class SchematicLabel
{
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
    public SchematicBox Box { get; init; }
    public double Height { get; init; }
    public LegendColor Color { get; init; }
    public string Layer { get; init; } = string.Empty;
    public string Text => string.Join(" / ", Lines);
    /// <summary>Rotulo de aviso (rojo en el DXF): se conserva en rojo oscuro.</summary>
    public bool IsWarning => Color.R > 180 && Color.G < 90 && Color.B < 90;
}

public sealed class SchematicEquipment
{
    public int Index { get; init; }
    public SchematicEquipmentKind Kind { get; init; }
    public string BlockName { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public double InsertX { get; init; }
    public double InsertY { get; init; }
    public SchematicBox Box { get; init; }
    public List<string> Attributes { get; } = new();
    public List<SchematicLabel> Labels { get; } = new();
}

public sealed class SchematicPipe
{
    public int Index { get; init; }
    public string Layer { get; init; } = string.Empty;
    public List<(double X, double Y)> Points { get; init; } = new();
    public int? From { get; set; }
    public int? To { get; set; }
    /// <summary>Diametros «liquido:gas[:gas]» en pulgadas.</summary>
    public SchematicLabel? Diameter { get; set; }
    /// <summary>Longitud «tramo / acumulada m».</summary>
    public SchematicLabel? Length { get; set; }
    public List<SchematicLabel> OtherLabels { get; } = new();

    /// <summary>Numero de tubos del tramo (2 = liquido + gas; 3 en recuperacion de calor).</summary>
    public int TubeCount
    {
        get
        {
            var d = Diameter?.Lines.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(d)) return 2;
            int n = d!.Split(':').Count(s => s.Trim().Length > 0);
            return Math.Clamp(n, 1, 4);
        }
    }
}

/// <summary>Esquema interpretado.</summary>
public sealed class SchematicDiagram
{
    public string FilePath { get; init; } = string.Empty;
    /// <summary>Perfil reconocido (null = no se reconoce el origen: solo copia fiel).</summary>
    public string? ProfileName { get; init; }
    /// <summary>Dibujo aplanado tal cual (copia fiel y vista previa del original).</summary>
    public LegendDrawing Source { get; init; } = new();
    public List<SchematicEquipment> Equipment { get; } = new();
    public List<SchematicPipe> Pipes { get; } = new();
    public List<SchematicLabel> FreeLabels { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>Altura de texto de referencia del DXF (mediana de rotulos).</summary>
    public double TextHeight { get; set; }

    public bool IsRecognized => ProfileName is not null && Equipment.Count > 0;

    public string Summary()
    {
        if (!IsRecognized) return "Origen no reconocido.";
        var porTipo = Equipment.GroupBy(e => e.Kind).OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key.Label().ToLowerInvariant()}");
        int conectados = Pipes.Count(p => p.From is not null && p.To is not null);
        return $"{ProfileName}: {string.Join(", ", porTipo)}; {Pipes.Count} tramos ({conectados} conectados por los dos extremos).";
    }
}

/// <summary>Formato de plano y opciones del estilo E3G.</summary>
public sealed class SchematicStyleOptions
{
    /// <summary>Area util del plano en mm (formato menos margenes y cajetin).</summary>
    public double PaperWidthMm { get; init; } = 780;
    public double PaperHeightMm { get; init; } = 540;
    /// <summary>Escala de la vista de dibujo (las medidas en papel no dependen de ella).</summary>
    public int Scale { get; init; } = 50;
    /// <summary>Altura (TEXT_SIZE) del tipo de texto de los rotulos, en papel.  No se modifica nunca.</summary>
    public double TextMm { get; init; } = 2.0;
    /// <summary>Altura del tipo de texto del titulo y de los rotulos de simbolo (UE, CR); 0 = 1,75 × TextMm.</summary>
    public double TitleTextMm { get; init; }
    /// <summary>Hay tipo de texto en negrita para la primera linea de las fichas.</summary>
    public bool BoldAvailable { get; init; } = true;
    public bool TitleBold { get; init; } = true;
    /// <summary>Factor de anchura del tipo de texto (para medir sin solapes).</summary>
    public double TextWidthFactor { get; init; } = 1.0;
    /// <summary>
    /// Tamaño de los simbolos: mm de papel que corresponden a la altura de texto del
    /// DXF.  Independiente del texto; se reduce automaticamente si no cabe.
    /// </summary>
    public double SymbolMm { get; init; } = 2.0;
    /// <summary>Compacta las distancias entre equipos (si no, solo se escala).</summary>
    public bool Compact { get; init; } = true;
    public string FontFamily { get; init; } = "Calibri";
    public string Title { get; init; } = "ESQUEMA DE PRINCIPIO · CLIMATIZACIÓN";
}

/// <summary>Claves de <see cref="LegendText.StyleKey"/> que usa el esquema (tipo de texto de Revit por clave).</summary>
public static class SchematicTextStyles
{
    public const string Regular = "texto";
    public const string Bold = "negrita";
    public const string Title = "titulo";
}

/// <summary>Resultado: dibujo E3G listo para el generador de Revit (unidades = mm reales).</summary>
public sealed class SchematicComposition
{
    public LegendDrawing Drawing { get; init; } = new();
    public IReadOnlyDictionary<string, string> LineStyleNames { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, int> LineWeights { get; init; } = new Dictionary<string, int>();
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public double CompactX { get; init; }
    public double CompactY { get; init; }
    public double TextMm { get; init; }
    public bool Fits { get; init; }
    public List<string> Notes { get; } = new();
}
