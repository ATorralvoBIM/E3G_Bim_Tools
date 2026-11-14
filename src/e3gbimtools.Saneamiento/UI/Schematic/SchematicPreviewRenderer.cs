// ============================================================================
//  SchematicPreviewRenderer.cs
//  Renderiza el SchematicLayout sobre un Canvas WPF.  Convierte coordenadas
//  en milímetros a píxeles con un factor configurable.  El zoom se aplica
//  al Canvas vía LayoutTransform desde fuera; aquí solo pintamos.
//
//  Conversión mm → px:
//    1 mm = 4 px (heurística: dibujo cómodo a 100 % de zoom; ajustable).
//
//  Sincronización con Revit:
//    Si se pasa un IRevitSyncService, los glifos de pipe son clicables y
//    seleccionan el elemento correspondiente en Revit.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using E3G.Saneamiento.Core.Schematic;
using E3G.Saneamiento.RevitBridge.Sync;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.UI.Schematic
{
    public sealed class SchematicPreviewRenderer
    {
        private const double PxPerMm = 4.0;
        private readonly Canvas _canvas;
        private readonly SchematicLayout _layout;
        private readonly IRevitSyncService _sync;
        private readonly Dictionary<ResultRow, FrameworkElement> _glyphs
            = new Dictionary<ResultRow, FrameworkElement>();

        public SchematicPreviewRenderer(Canvas canvas, SchematicLayout layout,
            IRevitSyncService sync)
        {
            _canvas = canvas;
            _layout = layout;
            _sync = sync;
        }

        public void Render()
        {
            _canvas.Children.Clear();
            _glyphs.Clear();
            if (_layout == null) return;

            _canvas.Width = _layout.WidthMm * PxPerMm;
            _canvas.Height = _layout.HeightMm * PxPerMm;

            var sysBrush = Brush(_layout.Params.SystemRgb);
            var critBrush = Brush(_layout.Params.CriticalPathRgb);

            // Conexiones
            foreach (var c in _layout.Connections)
            {
                var stroke = c.OnCriticalPath ? critBrush : sysBrush;
                double thick = c.OnCriticalPath ? 3.0 : 2.0;
                if (c.Kind == SchematicConnectionKind.Continuation)
                {
                    AddLine(c.From, c.To, stroke, thick, dashed: false);
                }
                else
                {
                    AddLine(c.From, c.To, stroke, 1.4, dashed: true);
                }
            }

            // Fuente
            AddSource(_layout.SourcePos, sysBrush);

            // Glifos
            foreach (var n in _layout.Nodes)
            {
                var stroke = n.OnCriticalPath ? critBrush : sysBrush;
                if (!n.IsFitting)
                {
                    var arrow = AddArrow(n.Pos, stroke, n.Row);
                    _glyphs[n.Row] = arrow;
                }
                else if (n.IsTerminal)
                {
                    var sq = AddTerminal(n.Pos, stroke, n.Row);
                    _glyphs[n.Row] = sq;
                }
                else if (!n.IsBifurcation)
                {
                    AddFittingMark(n.Pos, n.Row?.EndNodeKind, stroke);
                }
            }

            // Junctions
            foreach (var j in _layout.Junctions)
            {
                var stroke = j.OnCriticalPath ? critBrush : sysBrush;
                if (j.ParentFitting?.EndNodeKind == "TEE")
                    AddTee(j.Pos, stroke);
                else if (j.ParentFitting?.EndNodeKind == "CROSS")
                    AddCross(j.Pos, stroke);
                else
                    AddDot(j.Pos, stroke);
            }

            // Etiquetas
            foreach (var n in _layout.Nodes)
            {
                if (string.IsNullOrEmpty(n.Label)) continue;
                var labelX = n.Pos.XMm + (n.IsFitting ? 6.0 : 5.0);
                var labelY = n.Pos.YMm - 1.5;
                AddText(new SchematicPoint(labelX, labelY), n.Label,
                    n.IsFitting ? Brushes.Gray : Brushes.Black,
                    fontSize: 10, italic: n.IsFitting);
            }

            // Leyenda
            if (_layout.Params.ShowLegend && _layout.Legend != null)
            {
                var pos = new SchematicPoint(
                    _layout.Params.MmTrunkX,
                    _layout.HeightMm - 40);
                var text = BuildLegendText(_layout.Legend);
                AddText(pos, text, Brushes.DimGray, fontSize: 11, italic: false);
            }
        }

        // ── Selección al clicar ──────────────────────────────────────────

        public FrameworkElement GlyphFor(ResultRow row)
            => _glyphs.TryGetValue(row, out var g) ? g : null;

        private void HookSelection(FrameworkElement el, ResultRow row)
        {
            if (row == null) return;
            el.Tag = row;
            el.Cursor = Cursors.Hand;
            el.MouseLeftButtonDown += (s, e) =>
            {
                if (_sync != null) _sync.SelectInRevit(row, zoom: false);
                e.Handled = true;
            };
        }

        // ── Primitivas WPF ───────────────────────────────────────────────

        private void AddLine(SchematicPoint a, SchematicPoint b, Brush stroke,
            double thickness, bool dashed)
        {
            var ln = new Line
            {
                X1 = a.XMm * PxPerMm, Y1 = a.YMm * PxPerMm,
                X2 = b.XMm * PxPerMm, Y2 = b.YMm * PxPerMm,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            if (dashed) ln.StrokeDashArray = new DoubleCollection { 3, 3 };
            _canvas.Children.Add(ln);
        }

        private void AddSource(SchematicPoint pos, Brush brush)
        {
            double x = pos.XMm * PxPerMm;
            double y = pos.YMm * PxPerMm;
            double s = 6;
            var poly = new Polygon
            {
                Points = new PointCollection {
                    new Point(x - s, y - s),
                    new Point(x + s, y - s),
                    new Point(x, y + s),
                },
                Fill = brush,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
            };
            _canvas.Children.Add(poly);
            var lbl = new TextBlock
            {
                Text = "Fuente",
                FontSize = 10,
                Foreground = brush,
                FontWeight = FontWeights.SemiBold,
            };
            Canvas.SetLeft(lbl, x + s + 4);
            Canvas.SetTop(lbl, y - 8);
            _canvas.Children.Add(lbl);
        }

        private FrameworkElement AddArrow(SchematicPoint pos, Brush brush, ResultRow row)
        {
            double x = pos.XMm * PxPerMm;
            double y = pos.YMm * PxPerMm;
            var poly = new Polygon
            {
                Points = new PointCollection {
                    new Point(x - 3, y - 3),
                    new Point(x + 3, y - 3),
                    new Point(x, y + 3),
                },
                Fill = brush,
                Stroke = Brushes.Black,
                StrokeThickness = 0.4,
                ToolTip = BuildTooltip(row),
            };
            _canvas.Children.Add(poly);
            HookSelection(poly, row);
            return poly;
        }

        private FrameworkElement AddTerminal(SchematicPoint pos, Brush brush, ResultRow row)
        {
            double x = pos.XMm * PxPerMm;
            double y = pos.YMm * PxPerMm;
            double s = 6;
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = s, Height = s, Fill = brush,
                Stroke = Brushes.Black, StrokeThickness = 0.5,
                ToolTip = BuildTooltip(row),
            };
            Canvas.SetLeft(rect, x - s / 2);
            Canvas.SetTop(rect, y - s / 2);
            _canvas.Children.Add(rect);
            HookSelection(rect, row);
            return rect;
        }

        private void AddTee(SchematicPoint pos, Brush brush)
        {
            double x = pos.XMm * PxPerMm;
            double y = pos.YMm * PxPerMm;
            double s = 5;
            _canvas.Children.Add(new Line
            {
                X1 = x - s, Y1 = y, X2 = x + s, Y2 = y,
                Stroke = brush, StrokeThickness = 2,
            });
            _canvas.Children.Add(new Line
            {
                X1 = x, Y1 = y, X2 = x, Y2 = y + s,
                Stroke = brush, StrokeThickness = 2,
            });
        }

        private void AddCross(SchematicPoint pos, Brush brush)
        {
            double x = pos.XMm * PxPerMm;
            double y = pos.YMm * PxPerMm;
            double s = 5;
            _canvas.Children.Add(new Line
            {
                X1 = x - s, Y1 = y, X2 = x + s, Y2 = y,
                Stroke = brush, StrokeThickness = 2,
            });
            _canvas.Children.Add(new Line
            {
                X1 = x, Y1 = y - s, X2 = x, Y2 = y + s,
                Stroke = brush, StrokeThickness = 2,
            });
        }

        private void AddDot(SchematicPoint pos, Brush brush)
        {
            double x = pos.XMm * PxPerMm;
            double y = pos.YMm * PxPerMm;
            var el = new Ellipse
            {
                Width = 8, Height = 8, Fill = brush,
                Stroke = Brushes.White, StrokeThickness = 1.2,
            };
            Canvas.SetLeft(el, x - 4);
            Canvas.SetTop(el, y - 4);
            _canvas.Children.Add(el);
        }

        private void AddFittingMark(SchematicPoint pos, string kind, Brush brush)
        {
            double x = pos.XMm * PxPerMm;
            double y = pos.YMm * PxPerMm;
            double s = 4;
            if (kind == "ELBOW")
            {
                _canvas.Children.Add(new Line
                {
                    X1 = x, Y1 = y - s, X2 = x, Y2 = y,
                    Stroke = brush, StrokeThickness = 1.8,
                });
                _canvas.Children.Add(new Line
                {
                    X1 = x, Y1 = y, X2 = x + s, Y2 = y,
                    Stroke = brush, StrokeThickness = 1.8,
                });
            }
            else if (kind == "REDUCER")
            {
                var poly = new Polygon
                {
                    Points = new PointCollection {
                        new Point(x - s, y - s/2),
                        new Point(x + s, y - s/3),
                        new Point(x + s, y + s/3),
                        new Point(x - s, y + s/2),
                    },
                    Fill = Brushes.Transparent,
                    Stroke = brush,
                    StrokeThickness = 1.4,
                };
                _canvas.Children.Add(poly);
            }
        }

        private void AddText(SchematicPoint pos, string text, Brush brush,
            double fontSize, bool italic)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = brush,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            };
            Canvas.SetLeft(tb, pos.XMm * PxPerMm);
            Canvas.SetTop(tb, pos.YMm * PxPerMm - 8);
            _canvas.Children.Add(tb);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static Brush Brush((byte R, byte G, byte B) c)
        {
            var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private static string BuildTooltip(ResultRow row)
        {
            if (row == null) return null;
            if (row.IsFitting)
                return $"{row.EndNodeKind} en {row.NodePath}";
            return $"Tramo #{row.BfsIndex} · {row.NodePath}\n{row.Size}  ·  Q={row.FlowLps:F2} L/s  ·  v={row.Velocity:F2} m/s\nΔP/m={row.FrictionPaPm:F0} Pa/m  ·  ΔP tot={row.TotalLoss:F0} Pa\n{row.Status}";
        }

        private static string BuildLegendText(SchematicLegend l)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Sistema: {l.SystemName}");
            if (!string.IsNullOrEmpty(l.CriteriaName))
                sb.AppendLine($"Criterio: {l.CriteriaName}");
            sb.AppendLine($"Tramos: {l.PipeCount}  ·  ΔP peor caso: {l.WorstCaseDpPa:F0} Pa");
            sb.AppendLine($"Avisos: {l.WarningCount}  ·  Errores: {l.ErrorCount}");
            sb.Append($"Fecha: {l.GeneratedAt:yyyy-MM-dd HH:mm}");
            return sb.ToString();
        }
    }
}
