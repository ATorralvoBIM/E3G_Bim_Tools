// ============================================================================
//  HoverHighlightController.cs
//  Mientras el cursor permanece sobre una fila > 200 ms, manda a Revit
//  resaltar ese elemento.  Si el cursor sale antes, no se dispara nada.
//  Si pasa a otra fila, se reinicia el contador para esa nueva fila.
//
//  Diseño con DispatcherTimer en el thread de UI; el resaltado lo aplica el
//  ElementHighlighter, que ya tiene su propio auto-revert.
// ============================================================================
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using E3G.Saneamiento.RevitBridge.Sync;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.UI.Sync
{
    public sealed class HoverHighlightController : IDisposable
    {
        private readonly DataGrid _grid;
        private readonly IRevitSyncService _sync;
        private readonly DispatcherTimer _hoverTimer;
        private ResultRow _pendingRow;
        private bool _disposed;

        /// <summary>Tiempo de hover antes de disparar el highlight.</summary>
        public int HoverDelayMs { get; set; } = 200;

        /// <summary>Duración del resaltado en Revit.</summary>
        public int HighlightDurationMs { get; set; } = 1500;

        public HoverHighlightController(DataGrid grid, IRevitSyncService sync)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _sync = sync ?? throw new ArgumentNullException(nameof(sync));

            _hoverTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(HoverDelayMs),
            };
            _hoverTimer.Tick += OnHoverTick;

            _grid.MouseMove += OnGridMouseMove;
            _grid.MouseLeave += OnGridMouseLeave;
        }

        private void OnGridMouseMove(object sender, MouseEventArgs e)
        {
            if (_disposed) return;
            var row = GetRowUnderMouse(e.GetPosition(_grid));
            if (ReferenceEquals(row, _pendingRow)) return;   // mismo row, sin cambio
            _pendingRow = row;
            _hoverTimer.Stop();
            if (row != null)
            {
                _hoverTimer.Interval = TimeSpan.FromMilliseconds(HoverDelayMs);
                _hoverTimer.Start();
            }
        }

        private void OnGridMouseLeave(object sender, MouseEventArgs e)
        {
            _hoverTimer.Stop();
            _pendingRow = null;
        }

        private void OnHoverTick(object sender, EventArgs e)
        {
            _hoverTimer.Stop();
            if (_disposed || _pendingRow == null) return;
            _sync.HighlightInRevit(_pendingRow, HighlightDurationMs);
        }

        private ResultRow GetRowUnderMouse(Point pos)
        {
            // Hit-test del visual tree para encontrar el DataGridRow.
            var hit = VisualTreeHelper.HitTest(_grid, pos);
            if (hit == null) return null;
            DependencyObject obj = hit.VisualHit;
            while (obj != null && !(obj is DataGridRow))
                obj = VisualTreeHelper.GetParent(obj);
            if (obj is DataGridRow dgr && dgr.Item is ResultRow row)
                return row;
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hoverTimer.Stop();
            _hoverTimer.Tick -= OnHoverTick;
            _grid.MouseMove -= OnGridMouseMove;
            _grid.MouseLeave -= OnGridMouseLeave;
        }
    }
}
