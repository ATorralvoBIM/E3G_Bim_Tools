namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Metricas de fuente para maquetar MTEXT y colocar TextNotes.  Core no usa WPF,
/// asi que la implementacion real (GlyphTypeface) vive en DiTools.UI; sin ella se
/// usa <see cref="ApproxLegendTextMeasurer"/>.
/// Todas las medidas son relativas a la ALTURA DE MAYUSCULAS, que es lo que
/// AutoCAD llama "altura" en un texto TrueType.
/// </summary>
public interface ILegendTextMeasurer
{
    /// <summary>Ancho del texto cuando la altura de mayusculas es <paramref name="capHeight"/>.</summary>
    double MeasureWidth(string text, string fontFamily, bool bold, bool italic, double capHeight);

    /// <summary>
    /// Distancia de la linea base al centro de la caja de texto de Revit, en
    /// alturas de mayusculas.  Revit centra la caja con las metricas win
    /// (ascendente + descendente), no con la altura de mayusculas.
    /// </summary>
    double BoxCenterAboveBaseline(string fontFamily);

    /// <summary>Descendente bajo la linea base, en alturas de mayusculas.</summary>
    double Descent(string fontFamily);

    /// <summary>
    /// Resuelve el archivo de fuente de un estilo de texto (p. ej. "calibrib.ttf")
    /// a familia + negrita/cursiva.  Las fuentes SHX caen a Arial.
    /// </summary>
    (string Family, bool Bold, bool Italic) ResolveFont(string? fontFile);
}

/// <summary>Medidor aproximado (sin WPF) para tests y como reserva.</summary>
public sealed class ApproxLegendTextMeasurer : ILegendTextMeasurer
{
    public double MeasureWidth(string text, string fontFamily, bool bold, bool italic, double capHeight) =>
        text.Length * capHeight * 0.72;

    public double BoxCenterAboveBaseline(string fontFamily) => 0.54;

    public double Descent(string fontFamily) => 0.33;

    public (string Family, bool Bold, bool Italic) ResolveFont(string? fontFile)
    {
        if (string.IsNullOrWhiteSpace(fontFile)) return ("Arial", false, false);
        var name = System.IO.Path.GetFileNameWithoutExtension(fontFile);
        if (fontFile.EndsWith(".shx", System.StringComparison.OrdinalIgnoreCase)) return ("Arial", false, false);
        return (char.ToUpperInvariant(name[0]) + name.Substring(1), false, false);
    }
}
