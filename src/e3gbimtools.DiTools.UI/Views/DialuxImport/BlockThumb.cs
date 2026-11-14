using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using E3G.BimTools.DiTools.Core.Models.DialuxImport;

namespace E3G.BimTools.DiTools.UI.Views.DialuxImport;

/// <summary>Miniatura del simbolo de un bloque (sus trazos encajados en el control, Y hacia arriba).</summary>
public sealed class BlockThumb : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<DialuxSegment>), typeof(BlockThumb),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen Stroke = CreatePen();

    public IReadOnlyList<DialuxSegment>? Segments
    {
        get => (IReadOnlyList<DialuxSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        var segs = Segments;
        if (segs is null || segs.Count == 0 || ActualWidth < 4 || ActualHeight < 4) return;

        double minX = segs.Min(s => Math.Min(s.X1, s.X2)), maxX = segs.Max(s => Math.Max(s.X1, s.X2));
        double minY = segs.Min(s => Math.Min(s.Y1, s.Y2)), maxY = segs.Max(s => Math.Max(s.Y1, s.Y2));
        double w = Math.Max(maxX - minX, 1e-9), h = Math.Max(maxY - minY, 1e-9);
        const double pad = 3;
        double scale = Math.Min((ActualWidth - 2 * pad) / w, (ActualHeight - 2 * pad) / h);
        double ox = (ActualWidth - w * scale) / 2, oy = (ActualHeight - h * scale) / 2;

        Point P(double x, double y) => new(ox + (x - minX) * scale, ActualHeight - (oy + (y - minY) * scale));
        foreach (var s in segs)
            dc.DrawLine(Stroke, P(s.X1, s.Y1), P(s.X2, s.Y2));
    }

    private static Pen CreatePen()
    {
        var b = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
        b.Freeze();
        var p = new Pen(b, 1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }
}
