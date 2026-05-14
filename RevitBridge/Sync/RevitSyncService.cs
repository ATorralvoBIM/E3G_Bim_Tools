// ============================================================================
//  RevitSyncService.cs
//  Hub central de sincronización.  Une los componentes:
//    - RevitEventBridge    (escucha SelectionChanged)
//    - RevitSelectionAdapter (lee/escribe selección)
//    - ElementHighlighter   (overrides visuales)
//    - RowElementMap        (bimap row ↔ id)
//    - ExternalEventBus     (acciones contra Revit)
//  Y los expone a la UI vía IRevitSyncService.
//
//  Re-entry guard:
//    Cuando la UI selecciona una fila, llamamos a SelectInRevit() que
//    encola un SetCurrent en Revit.  Cuando Revit ejecuta ese cambio, salta
//    SelectionChanged → llegaría de vuelta al SyncService que querría
//    informar a la UI → la UI re-seleccionaría → bucle.  Lo cortamos con un
//    flag _suppressNext que ignora una vuelta del ciclo.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.RevitBridge.Sync
{
    public sealed class RevitSyncService : IRevitSyncService
    {
        private readonly UIApplication _uiApp;
        private readonly RowElementMap _map;
        private readonly ExternalEventBus _bus;
        private readonly RevitSelectionAdapter _selection;
        private readonly ElementHighlighter _highlighter;
        private readonly RevitEventBridge _events;

        // Flag para cortar el bucle Revit → UI → Revit cuando nosotros mismos
        // hemos provocado el cambio de selección en Revit.
        private bool _suppressNextRevitEvent;
        private bool _disposed;

        public event EventHandler<SyncSelectionEventArgs> SelectionChangedFromRevit;

        public RevitSyncService(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _map = new RowElementMap();
            _bus = new ExternalEventBus();
            _selection = new RevitSelectionAdapter(_uiApp, _bus);
            _highlighter = new ElementHighlighter(_bus);
            _events = new RevitEventBridge(_uiApp);
            _events.SelectionChanged += OnRevitSelectionChanged;
        }

        public void Start()
        {
            if (_disposed) return;
            _events.Subscribe();
        }

        public void Stop()
        {
            _events.Unsubscribe();
            _highlighter.ClearAll();
        }

        public void RegisterRow(ResultRow row, ElementId id)
            => _map.Register(row, id);

        public void ClearRows() => _map.Clear();

        public void SelectInRevit(ResultRow row, bool zoom)
        {
            if (row == null || _disposed) return;
            if (!_map.TryGetId(row, out var id)) return;

            // Vamos a provocar un cambio de selección en Revit; la próxima
            // vuelta del evento es ruido, la silenciamos.
            _suppressNextRevitEvent = true;

            if (zoom) _selection.ShowElement(id, alsoSelect: true);
            else _selection.SetCurrent(new[] { id });
        }

        public void HighlightInRevit(ResultRow row, int durationMs = 1500)
        {
            if (row == null || _disposed) return;
            if (!_map.TryGetId(row, out var id)) return;
            _highlighter.HighlightOnce(id, durationMs);
        }

        public void ClearHighlights() => _highlighter.ClearAll();

        private void OnRevitSelectionChanged(object sender, EventArgs e)
        {
            if (_disposed) return;

            // Si el cambio lo provocamos nosotros desde la UI, ignoramos.
            if (_suppressNextRevitEvent)
            {
                _suppressNextRevitEvent = false;
                return;
            }

            // Resolver las filas a partir de la selección actual de Revit.
            var ids = _selection.GetCurrent();
            if (ids == null || ids.Count == 0)
            {
                SelectionChangedFromRevit?.Invoke(this,
                    new SyncSelectionEventArgs(SelectionOrigin.Revit,
                        Array.Empty<ResultRow>()));
                return;
            }

            var rows = new List<ResultRow>(ids.Count);
            foreach (var id in ids)
            {
                if (_map.TryGetRow(id, out var row)) rows.Add(row);
            }

            // Aunque la selección Revit contenga elementos que no son de
            // nuestra red, propagamos lo que conozcamos (lista vacía si
            // ninguno casa).  La UI decidirá qué hacer.
            SelectionChangedFromRevit?.Invoke(this,
                new SyncSelectionEventArgs(SelectionOrigin.Revit, rows));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _events.SelectionChanged -= OnRevitSelectionChanged;
            _events.Dispose();
            _highlighter.Dispose();
            _bus.Dispose();
            _map.Clear();
            SelectionChangedFromRevit = null;
        }
    }
}
