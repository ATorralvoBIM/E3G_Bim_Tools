using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace E3G.BimTools.DiTools.Ribbon;

/// <summary>
/// Iconos del ribbon generados a partir de los trazos de Lucide
/// (https://lucide.dev, licencia ISC).  Cada icono es un conjunto de
/// geometrias sobre el viewBox 24x24 estandar de Lucide, renderizado como
/// <see cref="DrawingImage"/> con trazo redondeado.  Al ser vectorial escala
/// nitido a cualquier tamaño del ribbon (16 / 32 px) sin PNG ni rasterizado.
/// </summary>
internal static class LucideIcons
{
    // Color de marca (Fluent Primary) usado para todos los iconos del ribbon.
    private static readonly Brush Stroke = Freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD)));

    private static readonly Dictionary<string, ImageSource> _cache = new();

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    private static Geometry P(string data) => Geometry.Parse(data);
    private static Geometry Rect(double x, double y, double w, double h, double r) =>
        new RectangleGeometry(new Rect(x, y, w, h), r, r);
    private static Geometry Line(double x1, double y1, double x2, double y2) =>
        new LineGeometry(new Point(x1, y1), new Point(x2, y2));
    private static Geometry Circle(double cx, double cy, double r) =>
        new EllipseGeometry(new Point(cx, cy), r, r);

    public static ImageSource? TryGet(string key)
    {
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (!Defs.TryGetValue(key, out var parts)) return null;

        var group = new GeometryGroup();
        foreach (var g in parts) group.Children.Add(g);

        var pen = new Pen(Stroke, 2.0)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap   = PenLineCap.Round,
            LineJoin     = PenLineJoin.Round,
        };

        var dg = new DrawingGroup();
        // Rectangulo transparente para fijar el lienzo a 24x24 (alineacion uniforme).
        dg.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(0, 0, 24, 24))));
        dg.Children.Add(new GeometryDrawing(null, pen, group));

        var img = new DrawingImage(dg);
        img.Freeze();
        _cache[key] = img;
        return img;
    }

    private static readonly Dictionary<string, Geometry[]> Defs = new()
    {
        ["info"] = new[]
        {
            Circle(12, 12, 10),
            P("M12 16v-4"),
            P("M12 8h.01"),
        },
        ["layers"] = new[]
        {
            P("M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z"),
            P("M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12"),
            P("M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17"),
        },
        ["funnel"] = new[]
        {
            P("M10 20a1 1 0 0 0 .553.895l2 1A1 1 0 0 0 14 21v-7a2 2 0 0 1 .517-1.341L21.74 4.67A1 1 0 0 0 21 3H3a1 1 0 0 0-.742 1.67l7.225 7.989A2 2 0 0 1 10 14z"),
        },
        ["sliders"] = new[]
        {
            P("M10 5H3"), P("M12 19H3"), P("M14 3v4"), P("M16 17v4"),
            P("M21 12h-9"), P("M21 19h-5"), P("M21 5h-7"), P("M8 10v4"), P("M8 12H3"),
        },
        ["layout"] = new[]
        {
            Rect(3, 3, 18, 7, 1),
            Rect(3, 14, 9, 7, 1),
            Rect(16, 14, 5, 7, 1),
        },
        ["box"] = new[]
        {
            P("M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"),
            P("m3.3 7 8.7 5 8.7-5"),
            P("M12 22V12"),
        },
        ["fileplus"] = new[]
        {
            P("M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z"),
            P("M14 2v5a1 1 0 0 0 1 1h5"),
            P("M9 15h6"),
            P("M12 18v-6"),
        },
        ["boxes"] = new[]
        {
            P("M2.97 12.92A2 2 0 0 0 2 14.63v3.24a2 2 0 0 0 .97 1.71l3 1.8a2 2 0 0 0 2.06 0L12 19v-5.5l-5-3-4.03 2.42Z"),
            P("m7 16.5-4.74-2.85"),
            P("m7 16.5 5-3"),
            P("M7 16.5v5.17"),
            P("M12 13.5V19l3.97 2.38a2 2 0 0 0 2.06 0l3-1.8a2 2 0 0 0 .97-1.71v-3.24a2 2 0 0 0-.97-1.71L17 10.5l-5 3Z"),
            P("m17 16.5-5-3"),
            P("m17 16.5 4.74-2.85"),
            P("M17 16.5v5.17"),
            P("M7.97 4.42A2 2 0 0 0 7 6.13v4.37l5 3 5-3V6.13a2 2 0 0 0-.97-1.71l-3-1.8a2 2 0 0 0-2.06 0l-3 1.8Z"),
            P("M12 8 7.26 5.15"),
            P("m12 8 4.74-2.85"),
            P("M12 13.5V8"),
        },
        ["palette"] = new[]
        {
            P("M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z"),
            Circle(13.5, 6.5, 0.5),
            Circle(17.5, 10.5, 0.5),
            Circle(6.5, 12.5, 0.5),
            Circle(8.5, 7.5, 0.5),
        },
        // Lucide "list-tree"-like: simbolos a la izquierda + lineas de texto (leyenda).
        ["legend"] = new[]
        {
            Rect(3, 4, 4, 4, 1),
            P("M11 6h10"),
            Line(3, 12, 7, 12),
            P("M11 12h10"),
            Circle(5, 18, 2),
            P("M11 18h10"),
        },
        // Lucide "lamp-ceiling": luminaria colgada (DIALux → Revit).
        ["lamp"] = new[]
        {
            P("M12 2v5"),
            P("M6 7h12l4 9H2l4-9Z"),
            P("M9.17 16a3 3 0 1 0 5.66 0"),
        },
        // Lucide "file-spreadsheet": tabla → Excel.
        ["sheetexport"] = new[]
        {
            P("M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"),
            P("M14 2v4a2 2 0 0 0 2 2h4"),
            P("M8 13h2"),
            P("M14 13h2"),
            P("M8 17h2"),
            P("M14 17h2"),
        },
        ["table"] = new[]
        {
            P("M12 3v18"),
            Rect(3, 3, 18, 18, 2),
            P("M3 9h18"),
            P("M3 15h18"),
        },
        ["tag"] = new[]
        {
            P("M12.586 2.586A2 2 0 0 0 11.172 2H4a2 2 0 0 0-2 2v7.172a2 2 0 0 0 .586 1.414l8.704 8.704a2.426 2.426 0 0 0 3.42 0l6.58-6.58a2.426 2.426 0 0 0 0-3.42z"),
            P("M7.5 7.5h.01"),
        },
        ["link"] = new[]
        {
            P("M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"),
            P("M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"),
        },
        ["receipt"] = new[]
        {
            P("M13 16H8"),
            P("M14 8H8"),
            P("M16 12H8"),
            P("M4 3a1 1 0 0 1 1-1 1.3 1.3 0 0 1 .7.2l.933.6a1.3 1.3 0 0 0 1.4 0l.934-.6a1.3 1.3 0 0 1 1.4 0l.933.6a1.3 1.3 0 0 0 1.4 0l.933-.6a1.3 1.3 0 0 1 1.4 0l.934.6a1.3 1.3 0 0 0 1.4 0l.933-.6A1.3 1.3 0 0 1 19 2a1 1 0 0 1 1 1v18a1 1 0 0 1-1 1 1.3 1.3 0 0 1-.7-.2l-.933-.6a1.3 1.3 0 0 0-1.4 0l-.934.6a1.3 1.3 0 0 1-1.4 0l-.933-.6a1.3 1.3 0 0 0-1.4 0l-.933.6a1.3 1.3 0 0 1-1.4 0l-.934-.6a1.3 1.3 0 0 0-1.4 0l-.933.6a1.3 1.3 0 0 1-.7.2 1 1 0 0 1-1-1z"),
        },
        ["droplets"] = new[]
        {
            P("M7 16.3c2.2 0 4-1.83 4-4.05 0-1.16-.57-2.26-1.71-3.19S7.29 6.75 7 5.3c-.29 1.45-1.14 2.84-2.29 3.76S3 11.1 3 12.25c0 2.22 1.8 4.05 4 4.05z"),
            P("M12.56 6.6A10.97 10.97 0 0 0 14 3.02c.5 2.5 2 4.9 4 6.5s3 3.5 3 5.5a6.98 6.98 0 0 1-11.91 4.97"),
        },
        ["wind"] = new[]
        {
            P("M12.8 19.6A2 2 0 1 0 14 16H2"),
            P("M17.5 8a2.5 2.5 0 1 1 2 4H2"),
            P("M9.8 4.4A2 2 0 1 1 11 8H2"),
        },
        ["calculator"] = new[]
        {
            Rect(4, 2, 16, 20, 2),
            Line(8, 6, 16, 6),
            Line(16, 14, 16, 18),
            P("M16 10h.01"), P("M12 10h.01"), P("M8 10h.01"),
            P("M12 14h.01"), P("M8 14h.01"),
            P("M12 18h.01"), P("M8 18h.01"),
        },
        ["showerhead"] = new[]
        {
            P("m4 4 2.5 2.5"),
            P("M13.5 6.5a4.95 4.95 0 0 0-7 7"),
            P("M15 5 5 15"),
            P("M14 17v.01"), P("M10 16v.01"), P("M13 13v.01"), P("M16 10v.01"),
            P("M11 20v.01"), P("M17 14v.01"), P("M20 11v.01"),
        },
        ["settings"] = new[]
        {
            P("M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"),
            Circle(12, 12, 3),
        },
        ["flask"] = new[]
        {
            P("M14 2v6a2 2 0 0 0 .245.96l5.51 10.08A2 2 0 0 1 18 22H6a2 2 0 0 1-1.755-2.96l5.51-10.08A2 2 0 0 0 10 8V2"),
            P("M6.453 15h11.094"),
            P("M8.5 2h7"),
        },
    };
}
