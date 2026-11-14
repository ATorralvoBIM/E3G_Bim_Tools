using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Conversión entre <see cref="OverrideGraphicSettings"/> de un filtro en una
/// vista y <see cref="FilterGraphicsEntry"/> (JSON). Las lecturas y escrituras
/// requieren contexto de la API; las escrituras, además, transacción abierta.
/// </summary>
internal sealed class FilterGraphicsMapper
{
    /// <summary>Token para el patrón de línea continua (independiente del idioma).</summary>
    public const string SolidLineToken = "Solid";
    /// <summary>Token para el relleno sólido (su nombre cambia con el idioma de Revit).</summary>
    public const string SolidFillToken = "<Solid fill>";

    private readonly Document _doc;
    private readonly Dictionary<string, ElementId> _linePatterns;
    private readonly List<(string Name, FillPatternTarget Target, bool Solid, ElementId Id)> _fillPatterns;

    /// <summary>Patrones que no existen en el documento destino (para el informe).</summary>
    public HashSet<string> MissingPatterns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FilterGraphicsMapper(Document doc)
    {
        _doc = doc;

        _linePatterns = new FilteredElementCollector(doc)
            .OfClass(typeof(LinePatternElement))
            .Cast<LinePatternElement>()
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        _fillPatterns = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .Select(p =>
            {
                var fp = p.GetFillPattern();
                return (p.Name, fp.Target, fp.IsSolidFill, p.Id);
            })
            .ToList();
    }

    // ------------------------------------------------------------------ //
    //  Revit -> JSON
    // ------------------------------------------------------------------ //

    public FilterGraphicsEntry Read(View view, ElementId filterId, string filterName)
    {
        var ogs = view.GetFilterOverrides(filterId);

        return new FilterGraphicsEntry
        {
            Name         = filterName,
            Enabled      = SafeGet(() => view.GetIsFilterEnabled(filterId), true),
            Visible      = SafeGet(() => view.GetFilterVisibility(filterId), true),
            Halftone     = ogs.Halftone,
            Transparency = ogs.Transparency,

            ProjectionLine    = ReadLine(ogs.ProjectionLineColor, ogs.ProjectionLinePatternId, ogs.ProjectionLineWeight),
            SurfaceForeground = ReadPattern(ogs.IsSurfaceForegroundPatternVisible, ogs.SurfaceForegroundPatternColor, ogs.SurfaceForegroundPatternId),
            SurfaceBackground = ReadPattern(ogs.IsSurfaceBackgroundPatternVisible, ogs.SurfaceBackgroundPatternColor, ogs.SurfaceBackgroundPatternId),
            CutLine           = ReadLine(ogs.CutLineColor, ogs.CutLinePatternId, ogs.CutLineWeight),
            CutForeground     = ReadPattern(ogs.IsCutForegroundPatternVisible, ogs.CutForegroundPatternColor, ogs.CutForegroundPatternId),
            CutBackground     = ReadPattern(ogs.IsCutBackgroundPatternVisible, ogs.CutBackgroundPatternColor, ogs.CutBackgroundPatternId),
        };
    }

    private LineGraphics? ReadLine(Color color, ElementId patternId, int weight)
    {
        var g = new LineGraphics
        {
            Color   = ToHex(color),
            Pattern = LinePatternName(patternId),
            Weight  = weight == OverrideGraphicSettings.InvalidPenNumber ? null : weight,
        };
        return g.IsEmpty ? null : g;
    }

    private PatternGraphics? ReadPattern(bool visible, Color color, ElementId patternId)
    {
        string? name = null;
        string? target = null;
        if (patternId != ElementId.InvalidElementId && _doc.GetElement(patternId) is FillPatternElement fpe)
        {
            var fp = fpe.GetFillPattern();
            name   = fp.IsSolidFill ? SolidFillToken : fpe.Name;
            target = fp.Target.ToString();
        }

        var g = new PatternGraphics { Visible = visible, Color = ToHex(color), Pattern = name, Target = target };
        return g.IsEmpty ? null : g;
    }

    private string? LinePatternName(ElementId id)
    {
        if (id == ElementId.InvalidElementId) return null;
        if (id == LinePatternElement.GetSolidPatternId()) return SolidLineToken;
        return _doc.GetElement(id)?.Name;
    }

    // ------------------------------------------------------------------ //
    //  JSON -> Revit
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Construye un OverrideGraphicSettings completo desde la entrada. Lo no
    /// definido en el JSON queda "sin modificación" (por eso se parte de cero
    /// y no de los overrides actuales: el resultado es idéntico al exportado).
    /// </summary>
    public OverrideGraphicSettings Build(FilterGraphicsEntry e)
    {
        var ogs = new OverrideGraphicSettings();

        ogs.SetHalftone(e.Halftone);
        ogs.SetSurfaceTransparency(Math.Clamp(e.Transparency, 0, 100));

        if (e.ProjectionLine is { } pl)
        {
            if (ParseColor(pl.Color) is { } c) ogs.SetProjectionLineColor(c);
            if (ResolveLinePattern(pl.Pattern) is { } p) ogs.SetProjectionLinePatternId(p);
            if (pl.Weight is int w and >= 1 and <= 16) ogs.SetProjectionLineWeight(w);
        }
        if (e.CutLine is { } cl)
        {
            if (ParseColor(cl.Color) is { } c) ogs.SetCutLineColor(c);
            if (ResolveLinePattern(cl.Pattern) is { } p) ogs.SetCutLinePatternId(p);
            if (cl.Weight is int w and >= 1 and <= 16) ogs.SetCutLineWeight(w);
        }

        if (e.SurfaceForeground is { } sf)
        {
            ogs.SetSurfaceForegroundPatternVisible(sf.Visible);
            if (ParseColor(sf.Color) is { } c) ogs.SetSurfaceForegroundPatternColor(c);
            if (ResolveFillPattern(sf) is { } p) ogs.SetSurfaceForegroundPatternId(p);
        }
        if (e.SurfaceBackground is { } sb)
        {
            ogs.SetSurfaceBackgroundPatternVisible(sb.Visible);
            if (ParseColor(sb.Color) is { } c) ogs.SetSurfaceBackgroundPatternColor(c);
            if (ResolveFillPattern(sb) is { } p) ogs.SetSurfaceBackgroundPatternId(p);
        }
        if (e.CutForeground is { } cf)
        {
            ogs.SetCutForegroundPatternVisible(cf.Visible);
            if (ParseColor(cf.Color) is { } c) ogs.SetCutForegroundPatternColor(c);
            if (ResolveFillPattern(cf) is { } p) ogs.SetCutForegroundPatternId(p);
        }
        if (e.CutBackground is { } cb)
        {
            ogs.SetCutBackgroundPatternVisible(cb.Visible);
            if (ParseColor(cb.Color) is { } c) ogs.SetCutBackgroundPatternColor(c);
            if (ResolveFillPattern(cb) is { } p) ogs.SetCutBackgroundPatternId(p);
        }

        return ogs;
    }

    private ElementId? ResolveLinePattern(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (string.Equals(name, SolidLineToken, StringComparison.OrdinalIgnoreCase))
            return LinePatternElement.GetSolidPatternId();
        if (_linePatterns.TryGetValue(name, out var id)) return id;

        MissingPatterns.Add($"Línea: {name}");
        return null;
    }

    private ElementId? ResolveFillPattern(PatternGraphics g)
    {
        if (string.IsNullOrWhiteSpace(g.Pattern)) return null;

        FillPatternTarget? target = Enum.TryParse<FillPatternTarget>(g.Target, true, out var t) ? t : null;
        bool solid = string.Equals(g.Pattern, SolidFillToken, StringComparison.OrdinalIgnoreCase);

        var candidates = _fillPatterns.Where(p => solid
            ? p.Solid
            : string.Equals(p.Name, g.Pattern, StringComparison.OrdinalIgnoreCase)).ToList();

        var match = candidates.FirstOrDefault(p => target is null || p.Target == target);
        if (match.Id is null) match = candidates.FirstOrDefault();
        if (match.Id is not null) return match.Id;

        MissingPatterns.Add($"Relleno: {g.Pattern}");
        return null;
    }

    // ------------------------------------------------------------------ //

    public static string? ToHex(Color? c)
        => c is { IsValid: true } ? $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}" : null;

    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return null;
        return new Color((byte)(v >> 16), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
    }

    private static T SafeGet<T>(Func<T> f, T fallback)
    {
        try { return f(); } catch { return fallback; }
    }
}
