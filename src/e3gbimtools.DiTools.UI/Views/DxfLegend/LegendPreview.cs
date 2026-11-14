using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;
using E3G.BimTools.DiTools.Core.Services.DxfLegend;

namespace E3G.BimTools.DiTools.UI.Views.DxfLegend;

/// <summary>
/// Vista previa del dibujo aplanado: pinta rellenos, curvas y textos tal y
/// como se van a crear en Revit (con el filtro de capas aplicado), ajustada al
/// espacio disponible sobre fondo blanco de papel.
///
/// Rueda = zoom en el cursor, arrastrar = desplazar, doble clic = encuadrar
/// (2026-11-12, para los esquemas de principio, mucho mayores que una leyenda).
/// El dibujo se cachea en un DrawingGroup: desplazar no lo recalcula; el zoom
/// si, para mantener las lineas a 1 px.
/// </summary>
public sealed class LegendPreview : FrameworkElement
{
    private const double Pad = 12;
    private const double MaxZoom = 400;

    private LegendDrawing? _drawing;
    private ISet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, bool, bool), (Typeface Face, double Caps)> _faces = new();
    private readonly Dictionary<LegendColor, Brush> _brushes = new();

    private DrawingGroup? _cache;
    private Size _cacheSize;
    private double _cacheZoom;

    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragFrom;

    public LegendPreview()
    {
        ClipToBounds = true;
        Cursor = Cursors.SizeAll;
        ToolTip = "Rueda: zoom · Arrastrar: desplazar · Doble clic: encuadrar";
    }

    public void SetDrawing(LegendDrawing? drawing, ISet<string> excludedLayers)
    {
        bool nuevo = !ReferenceEquals(drawing, _drawing);
        _drawing = drawing;
        _excluded = excludedLayers;
        if (nuevo) ResetView();
        _cache = null;
        InvalidateVisual();
    }

    /// <summary>Vuelve a encuadrar todo el dibujo.</summary>
    public void ResetView()
    {
        _zoom = 1;
        _pan = new Vector();
        _cache = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var area = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.White, null, area);
        if (_drawing is null || ActualWidth < 2 * Pad || ActualHeight < 2 * Pad) return;

        var size = new Size(ActualWidth, ActualHeight);
        if (_cache is null || _cacheSize != size || _cacheZoom != _zoom)
        {
            _cache = BuildDrawing(size, _zoom);
            _cacheSize = size;
            _cacheZoom = _zoom;
        }
        if (_cache is null) return;

        dc.PushClip(new RectangleGeometry(area));
        dc.PushTransform(new MatrixTransform(_zoom, 0, 0, _zoom, _pan.X, _pan.Y));
        dc.DrawDrawing(_cache);
        dc.Pop();
        dc.Pop();
    }

    // ============================================================
    // Zoom / desplazamiento
    // ============================================================

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_drawing is null) return;
        double f = e.Delta > 0 ? 1.25 : 1 / 1.25;
        double z = Math.Clamp(_zoom * f, 1, MaxZoom);
        if (z == _zoom) { e.Handled = true; return; }
        // El punto bajo el cursor se queda quieto: p = pan + zoom·q.
        var p = e.GetPosition(this);
        var q = (p - (Point)_pan) / _zoom;
        _zoom = z;
        _pan = (Vector)p - q * _zoom;
        if (_zoom == 1) _pan = new Vector();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
        _dragFrom = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is not { } from || !IsMouseCaptured) return;
        var p = e.GetPosition(this);
        _pan += p - from;
        _dragFrom = p;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragFrom = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    // ============================================================
    // Dibujo
    // ============================================================

    /// <summary>Dibujo encuadrado en <paramref name="size"/>; lineas de 1/zoom para que salgan a 1 px.</summary>
    private DrawingGroup? BuildDrawing(Size size, double zoom)
    {
        var items = _drawing!.Items.Where(i => !_excluded.Contains(i.Layer)).ToList();
        var b = LegendGeometry.Bounds(items);
        if (b is null) return null;
        var (minX, minY, maxX, maxY) = b.Value;
        double w = Math.Max(maxX - minX, 1e-9), h = Math.Max(maxY - minY, 1e-9);
        double s = Math.Min((size.Width - 2 * Pad) / w, (size.Height - 2 * Pad) / h);
        double ox = (size.Width - w * s) / 2, oy = (size.Height - h * s) / 2;
        Point Map(double x, double y) => new(ox + (x - minX) * s, oy + (maxY - y) * s);

        // Con zoom se afina la teselacion de arcos para que no se vean poligonales.
        double arcStep = Math.PI / (zoom > 8 ? 72 : zoom > 2 ? 36 : 18);

        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            foreach (var f in items.OfType<LegendFill>())
            {
                var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
                using (var ctx = geo.Open())
                {
                    foreach (var loop in f.Loops)
                    {
                        var poly = LegendGeometry.LoopToPolygon(loop, Math.Max(w, h) * 1e-6, arcStep);
                        if (poly is null || poly.Count < 3) continue;
                        ctx.BeginFigure(Map(poly[0].X, poly[0].Y), true, true);
                        ctx.PolyLineTo(poly.Skip(1).Select(p => Map(p.X, p.Y)).ToList(), false, false);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(BrushOf(f.Color), null, geo);
            }

            foreach (var c in items.OfType<LegendCurve>())
            {
                var pts = LegendGeometry.Polygonize(c, arcStep);
                if (pts.Count < 2) continue;
                var pen = new Pen(BrushOf(c.Color), 1.0 / zoom);
                if (c.Pattern is not null)
                {
                    // DashStyle va en multiplos del grosor: trazo real × s × zoom (px).
                    var dashes = c.Pattern.Segments.Select(v => Math.Max(Math.Abs(v) * s * zoom, 0.5)).ToList();
                    if (dashes.Count % 2 == 1) dashes.Add(dashes[^1]);
                    pen.DashStyle = new DashStyle(dashes, 0);
                }
                pen.Freeze();
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(Map(pts[0].X, pts[0].Y), false, false);
                    ctx.PolyLineTo(pts.Skip(1).Select(p => Map(p.X, p.Y)).ToList(), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (var t in items.OfType<LegendText>())
            {
                var (face, caps) = Face(t.FontFamily, t.Bold, t.Italic);
                double em = t.Height * s / caps;
                if (em * zoom < 0.5) continue;
                var ft = new FormattedText(t.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, em, BrushOf(t.Color), dpi);
                double width = ft.Width * t.WidthFactor;
                double dxAlign = t.HAlign switch { LegendTextHAlign.Center => -width / 2, LegendTextHAlign.Right => -width, _ => 0 };
                var anchor = Map(t.X, t.BaselineY);

                dc.PushTransform(new TranslateTransform(anchor.X, anchor.Y));
                dc.PushTransform(new RotateTransform(-t.Rotation * 180 / Math.PI));
                dc.PushTransform(new ScaleTransform(t.WidthFactor, 1));
                dc.DrawText(ft, new Point(dxAlign / t.WidthFactor, -ft.Baseline));
                dc.Pop(); dc.Pop(); dc.Pop();
            }
        }
        group.Freeze();
        return group;
    }

    private Brush BrushOf(LegendColor c)
    {
        if (_brushes.TryGetValue(c, out var b)) return b;
        b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        _brushes[c] = b;
        return b;
    }

    private (Typeface, double) Face(string family, bool bold, bool italic)
    {
        var key = (family, bold, italic);
        if (_faces.TryGetValue(key, out var f)) return f;
        var tf = new Typeface(new FontFamily(family), italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        double caps = tf.TryGetGlyphTypeface(out var gt) && gt.CapsHeight > 0.2 ? gt.CapsHeight : 0.7;
        f = (tf, caps);
        _faces[key] = f;
        return f;
    }
}
