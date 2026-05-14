// ============================================================================
//  GridSelectionController.cs
//  Une la selección del DataGrid de MEPCalc con el servicio de sync.
//
//  Direcciones:
//    UI → Revit  : cuando el usuario selecciona una fila → sync.SelectInRevit
//    Revit → UI  : cuando sync notifica cambio → seleccionar la primera fila
//                  conocida y ScrollIntoView.
//
//  Re-entry:
//    Activamos _suppressUi cuando la selección de la UI viene provocada por
//    Revit, para que el handler SelectionChanged del DataGrid no vuelva a
//    pedir SelectInRevit y formar un bucle.
// ============================================================================
using System;
using System.Linq;
using System.Windows.Controls;
using E3G.Saneamiento.RevitBridge.Sync;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.UI.Sync
{
    public sealed class GridSelectionController : IDisposable
    {
        private readonly DataGrid _grid;
        private readonly IRevitSyncService _sync;
        private bool _suppressUi;
        private bool _disposed;

        /// <summary>
        /// Si true, además de seleccionar en Revit hace zoom-fit cuando el
        /// usuario hace doble-clic.  Single-click solo selecciona.
        /// </summary>
        public bool ZoomOnDoubleClick { get; set; } = true;

        public GridSelectionController(DataGrid grid, IRevitSyncService sync)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _sync = sync ?? throw new ArgumentNullException(nameof(sync));

            _grid.SelectionChanged += OnGridSelectionChanged;
            _grid.MouseDoubleClick += OnGridDoubleClick;
            _sync.SelectionChangedFromRevit += OnSyncSelectionFromRevit;
        }

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressUi || _disposed) return;
            if (_grid.SelectedItem is ResultRow row)
            {
                _sync.SelectInRevit(row, zoom: false);
            }
        }

        private void OnGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_disposed || !ZoomOnDoubleClick) return;
            if (_grid.SelectedItem is ResultRow row)
            {
                _sync.SelectInRevit(row, zoom: true);
            }
        }

        private void OnSyncSelectionFromRevit(object sender, SyncSelectionEventArgs e)
        {
            if (_disposed || e?.Rows == null || e.Rows.Count == 0) return;

            // Seleccionamos la primera fila reconocida (UX simple).  Si en el
            // futuro quieres multi-selección, basta con setear SelectedItems.
            var first = e.Rows.FirstOrDefault();
            if (first == null) return;

            _suppressUi = true;
            try
            {
                _grid.SelectedItem = first;
                _grid.ScrollIntoView(first);
            }
            finally { _suppressUi = false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _grid.SelectionChanged -= OnGridSelectionChanged;
            _grid.MouseDoubleClick -= OnGridDoubleClick;
            _sync.SelectionChangedFromRevit -= OnSyncSelectionFromRevit;
        }
    }
}
