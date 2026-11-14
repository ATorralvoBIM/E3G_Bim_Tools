using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using E3G.BimTools.DiTools.Core.Models.DialuxImport;

namespace E3G.BimTools.DiTools.UI.Views.DialuxImport;

/// <summary>
/// Planta esquematica de las luminarias del DWG: un circulo por insercion con
/// un trazo que marca su giro, coloreado por estado.  Encaja el conjunto en el
/// control (Y hacia arriba, como en AutoCAD).
/// </summary>
public sealed class DialuxPreview : FrameworkElement
{
    public readonly record struct Marker(double X, double Y, double Rotation, Brush Brush);

    public static readonly Brush Mapped = Frozen(0x0F, 0x6C, 0xBD);
    public static readonly Brush Unmapped = Frozen(0xB0, 0xB0, 0xB0);
    private static readonly Brush OkBrush = Frozen(0x10, 0x7C, 0x10);
    private static readonly Brush WarnBrush = Frozen(0xE0, 0x7A, 0x00);
    private static readonly Brush ErrorBrush = Frozen(0xC4, 0x2B, 0x1C);

    private IReadOnlyList<Marker> _markers = Array.Empty<Marker>();

    public static Brush ColorFor(DialuxRowStatus s) => s switch
    {
        DialuxRowStatus.Ok => OkBrush,
        DialuxRowStatus.New => Mapped,
        DialuxRowStatus.Error => ErrorBrush,
        _ => WarnBrush,
    };

    public void SetMarkers(IReadOnlyList<Marker> markers)
    {
        _markers = markers ?? Array.Empty<Marker>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_markers.Count == 0 || ActualWidth < 20 || ActualHeight < 20) return;

        double minX = _markers.Min(m => m.X), maxX = _markers.Max(m => m.X);
        double minY = _markers.Min(m => m.Y), maxY = _markers.Max(m => m.Y);
        double w = Math.Max(maxX - minX, 1e-9), h = Math.Max(maxY - minY, 1e-9);
        const double margin = 24;
        double scale = Math.Min((ActualWidth - 2 * margin) / w, (ActualHeight - 2 * margin) / h);
        if (_markers.Count == 1 || double.IsInfinity(scale)) scale = 1;
        double ox = (ActualWidth - w * scale) / 2, oy = (ActualHeight - h * scale) / 2;

        Point ToScreen(double x, double y) => new(ox + (x - minX) * scale, ActualHeight - (oy + (y - minY) * scale));

        // Tamaño del simbolo segun la densidad de luminarias.
        double r = Math.Clamp(Math.Sqrt((ActualWidth * ActualHeight) / Math.Max(1, _markers.Count)) / 8, 2, 7);
        foreach (var m in _markers.OrderBy(m => m.Brush == Unmapped ? 0 : 1))
        {
            var p = ToScreen(m.X, m.Y);
            var pen = new Pen(m.Brush, 1.2);
            dc.DrawEllipse(null, pen, p, r, r);
            var tip = new Point(p.X + Math.Cos(m.Rotation) * r * 1.8, p.Y - Math.Sin(m.Rotation) * r * 1.8);
            dc.DrawLine(pen, p, tip);
        }
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
