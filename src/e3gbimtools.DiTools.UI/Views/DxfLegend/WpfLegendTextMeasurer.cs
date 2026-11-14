using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.UI.Views.DxfLegend;

/// <summary>
/// Metricas reales de fuente via WPF (GlyphTypeface) para maquetar el MTEXT del
/// DXF igual que AutoCAD (que mide la altura como altura de mayusculas) y para
/// centrar los TextNotes de Revit (que centran la caja con las metricas win).
/// </summary>
public sealed class WpfLegendTextMeasurer : ILegendTextMeasurer
{
    private const double RefEm = 100.0;

    private sealed record FaceInfo(Typeface Typeface, double CapsRatio, double Baseline, double LineSpacing);

    private readonly Dictionary<(string, bool, bool), FaceInfo> _faces = new();
    private readonly Dictionary<string, (string, bool, bool)> _fontFiles = new(StringComparer.OrdinalIgnoreCase);

    public double MeasureWidth(string text, string fontFamily, bool bold, bool italic, double capHeight)
    {
        if (string.IsNullOrEmpty(text) || capHeight <= 0) return 0;
        var face = Face(fontFamily, bold, italic);
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            face.Typeface, RefEm, Brushes.Black, 1.0);
        return ft.Width / RefEm * (capHeight / face.CapsRatio);
    }

    public double BoxCenterAboveBaseline(string fontFamily)
    {
        var f = Face(fontFamily, false, false);
        double descent = f.LineSpacing - f.Baseline;
        return (f.Baseline - descent) / 2 / f.CapsRatio;
    }

    public double Descent(string fontFamily)
    {
        var f = Face(fontFamily, false, false);
        return (f.LineSpacing - f.Baseline) / f.CapsRatio;
    }

    public (string Family, bool Bold, bool Italic) ResolveFont(string? fontFile)
    {
        if (string.IsNullOrWhiteSpace(fontFile) || fontFile!.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
            return ("Arial", false, false);
        if (_fontFiles.TryGetValue(fontFile, out var cached)) return cached;

        (string, bool, bool) result;
        var path = FindFontFile(fontFile);
        try
        {
            if (path is null) throw new FileNotFoundException(fontFile);
            var gt = new GlyphTypeface(new Uri(path));
            string family = Pick(gt.Win32FamilyNames) ?? Pick(gt.FamilyNames) ?? Path.GetFileNameWithoutExtension(fontFile);
            bool bold = gt.Weight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();
            bool italic = gt.Style != FontStyles.Normal;
            // Win32FamilyNames incluye a veces el estilo ("Calibri Light" es una familia propia, se respeta).
            result = (family, bold, italic);
        }
        catch
        {
            var name = Path.GetFileNameWithoutExtension(fontFile);
            result = (name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : "Arial", false, false);
        }
        _fontFiles[fontFile] = result;
        return result;
    }

    private static string? Pick(IDictionary<CultureInfo, string> names)
    {
        if (names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var en)) return en;
        foreach (var v in names.Values) return v;
        return null;
    }

    private static string? FindFontFile(string file)
    {
        if (Path.IsPathRooted(file) && File.Exists(file)) return file;
        var name = Path.GetFileName(file);
        string[] dirs =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
        };
        foreach (var d in dirs)
        {
            var p = Path.Combine(d, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private FaceInfo Face(string family, bool bold, bool italic)
    {
        var key = (family, bold, italic);
        if (_faces.TryGetValue(key, out var f)) return f;

        var ff = new FontFamily(family);
        var tf = new Typeface(ff, italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        double caps = tf.TryGetGlyphTypeface(out var gt) && gt.CapsHeight > 0.2 ? gt.CapsHeight : 0.7;
        double baseline = ff.Baseline > 0 ? ff.Baseline : 0.9;
        double spacing = ff.LineSpacing > baseline ? ff.LineSpacing : baseline + 0.25;
        f = new FaceInfo(tf, caps, baseline, spacing);
        _faces[key] = f;
        return f;
    }
}
