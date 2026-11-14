using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Core.Services.DxfLegend;

/// <summary>
/// Maqueta un MTEXT de AutoCAD en lineas sueltas con su posicion de linea base.
///
/// Revit no deja fijar la interlinea de un TextNote, asi que para conservar las
/// proporciones del DXF cada linea se convierte en un TextNote propio.  Reglas
/// (contrastadas con AutoCAD/ezdxf sobre las leyendas E3G):
///   • Altura = altura de mayusculas.
///   • Interlinea base a base = factor × 5/3 × altura.
///   • Sangrias \pi (primera linea) y \pl (izquierda) en multiplos de la altura.
///   • Ajuste de linea por palabras contra el ancho del cuadro (0 = sin ajuste).
/// </summary>
public static class MTextLayout
{
    public sealed class Paragraph
    {
        public string Text { get; set; } = string.Empty;
        public double FirstIndent { get; set; }
        public double LeftIndent { get; set; }
    }

    public readonly record struct Line(string Text, double X, double BaselineY, double Width);

    /// <summary>Lineas en coordenadas locales: origen en el punto de insercion, sin giro.</summary>
    public static List<Line> Layout(
        string rawValue,
        double height,
        double boxWidth,
        double lineSpacingFactor,
        int attachment,
        ILegendTextMeasurer measurer,
        string font, bool bold, bool italic, double widthFactor)
    {
        var paragraphs = Parse(rawValue);
        var wrapped = new List<(string Text, double Indent)>();
        foreach (var p in paragraphs)
            Wrap(p, height, boxWidth, measurer, font, bold, italic, widthFactor, wrapped);
        if (wrapped.Count == 0) return new List<Line>();

        double spacing = (lineSpacingFactor > 0 ? lineSpacingFactor : 1.0) * 5.0 / 3.0 * height;
        double total = height + (wrapped.Count - 1) * spacing;

        // attachment: 1..3 arriba, 4..6 centro, 7..9 abajo; columna 1/2/3 = izq/centro/dcha.
        int row = (Math.Max(1, attachment) - 1) / 3;
        int col = (Math.Max(1, attachment) - 1) % 3;
        double firstBaseline = row switch
        {
            0 => -height,
            1 => total / 2 - height,
            _ => total - height,
        };

        var lines = new List<Line>(wrapped.Count);
        for (int i = 0; i < wrapped.Count; i++)
        {
            var (text, indent) = wrapped[i];
            double w = measurer.MeasureWidth(text, font, bold, italic, height) * widthFactor;
            // Centrado / derecha: la linea se alinea en el punto de insercion (sin sangria).
            double x = col == 0 ? indent * height : 0;
            lines.Add(new Line(text, x, firstBaseline - i * spacing, w));
        }
        return lines;
    }

    private static void Wrap(
        Paragraph p, double h, double boxWidth, ILegendTextMeasurer m,
        string font, bool bold, bool italic, double wf,
        List<(string, double)> output)
    {
        double firstIndent = p.LeftIndent + p.FirstIndent;
        if (boxWidth <= 0 || string.IsNullOrEmpty(p.Text))
        {
            output.Add((p.Text.TrimEnd(), firstIndent));
            return;
        }

        var words = p.Text.Split(' ');
        var current = new StringBuilder();
        double indent = firstIndent;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            double avail = boxWidth - indent * h;
            if (current.Length > 0 && m.MeasureWidth(candidate.TrimEnd(), font, bold, italic, h) * wf > avail)
            {
                output.Add((current.ToString().TrimEnd(), indent));
                current.Clear().Append(word);
                indent = p.LeftIndent;
            }
            else
            {
                current.Clear().Append(candidate);
            }
        }
        if (current.ToString().Trim().Length > 0 || output.Count == 0)
            output.Add((current.ToString().TrimEnd(), indent));
    }

    /// <summary>Quita los codigos de formato del MTEXT y separa parrafos con sus sangrias.</summary>
    public static List<Paragraph> Parse(string raw)
    {
        var result = new List<Paragraph>();
        var cur = new Paragraph();
        var sb = new StringBuilder();
        raw = ReplaceSpecials(raw ?? string.Empty);

        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c == '{' || c == '}') { i++; continue; }
            if (c != '\\' || i + 1 >= raw.Length)
            {
                sb.Append(c == '\n' ? ' ' : c);
                i++;
                continue;
            }

            char code = raw[i + 1];
            switch (code)
            {
                case 'P':
                    cur.Text = sb.ToString();
                    result.Add(cur);
                    cur = new Paragraph { FirstIndent = 0, LeftIndent = cur.LeftIndent };
                    sb.Clear();
                    i += 2;
                    break;
                case 'p':
                    {
                        int end = raw.IndexOf(';', i);
                        if (end < 0) end = raw.Length;
                        ApplyParagraphCodes(raw.Substring(i + 2, end - i - 2), cur);
                        i = end + 1;
                        break;
                    }
                case 'S':
                    {
                        int end = raw.IndexOf(';', i);
                        if (end < 0) end = raw.Length;
                        var stacked = raw.Substring(i + 2, end - i - 2);
                        sb.Append(stacked.Replace('^', '/').Replace('#', '/'));
                        i = end + 1;
                        break;
                    }
                case 'U':
                    if (i + 7 <= raw.Length && raw[i + 2] == '+'
                        && int.TryParse(raw.Substring(i + 3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp))
                    {
                        sb.Append((char)cp);
                        i += 7;
                    }
                    else i += 2;
                    break;
                case '~': sb.Append(' '); i += 2; break;
                case '\\': sb.Append('\\'); i += 2; break;
                case '{': sb.Append('{'); i += 2; break;
                case '}': sb.Append('}'); i += 2; break;
                case 'L': case 'l': case 'O': case 'o': case 'K': case 'k': case 'N':
                    i += 2;
                    break;
                case 'f': case 'F': case 'H': case 'W': case 'Q': case 'T': case 'A':
                case 'C': case 'c':
                    {
                        int end = raw.IndexOf(';', i);
                        i = end < 0 ? raw.Length : end + 1;
                        break;
                    }
                default:
                    sb.Append(code);
                    i += 2;
                    break;
            }
        }
        cur.Text = sb.ToString();
        result.Add(cur);
        return result;
    }

    private static void ApplyParagraphCodes(string codes, Paragraph p)
    {
        // Ej.: "xl0.66667" · "l0.1" · "xi-3,l3,t3" · "qc"
        if (codes.StartsWith("x", StringComparison.Ordinal)) codes = codes.Substring(1);
        foreach (var part in codes.Split(','))
        {
            if (part.Length < 2) continue;
            if (!double.TryParse(part.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
            switch (part[0])
            {
                case 'i': p.FirstIndent = v; break;
                case 'l': p.LeftIndent = v; break;
            }
        }
    }

    public static string ReplaceSpecials(string s) => s
        .Replace("%%c", "Ø").Replace("%%C", "Ø")
        .Replace("%%d", "°").Replace("%%D", "°")
        .Replace("%%p", "±").Replace("%%P", "±")
        .Replace("%%%", "%");
}
