// ============================================================================
//  RevitSelectionAdapter.cs
//  Adaptador para todas las operaciones de selección y vista de Revit.
//  Centraliza:
//    - Leer la selección actual (UIDocument.Selection.GetElementIds).
//    - Fijar la selección programáticamente (SetElementIds).
//    - Mostrar un elemento en la vista activa con zoom-fit (ShowElements).
//    - Limpiar la selección.
//
//  Todas las MUTACIONES se rutan a través de ExternalEventBus para que
//  ocurran en un contexto válido de la API de Revit.  Las LECTURAS son
//  síncronas y directas — son seguras desde el thread de UI siempre que la
//  ventana sea modal o no-modal con UIApplication válido.
// ============================================================================
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.Saneamiento.RevitBridge.Sync
{
    public sealed class RevitSelectionAdapter
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEventBus _bus;

        public RevitSelectionAdapter(UIApplication uiApp, ExternalEventBus bus)
        {
            _uiApp = uiApp;
            _bus = bus;
        }

        /// <summary>
        /// ElementIds actualmente seleccionados en el UIDocument activo.
        /// Devuelve lista vacía si no hay documento activo.
        /// </summary>
        public ICollection<ElementId> GetCurrent()
        {
            var uidoc = _uiApp?.ActiveUIDocument;
            if (uidoc == null) return new List<ElementId>();
            try { return uidoc.Selection.GetElementIds(); }
            catch { return new List<ElementId>(); }
        }

        /// <summary>
        /// Reemplaza la selección de Revit por la colección dada.  Se ejecuta
        /// en el siguiente turno de la API; el callback opcional se invoca
        /// tras completarse la operación.
        /// </summary>
        public void SetCurrent(ICollection<ElementId> ids,
                               System.Action onCompleted = null)
        {
            if (ids == null) ids = new List<ElementId>();
            _bus.Enqueue(app =>
            {
                var uidoc = app?.ActiveUIDocument;
                if (uidoc == null) { onCompleted?.Invoke(); return; }
                try { uidoc.Selection.SetElementIds(ids); }
                catch { /* documento cerrado o id inválido — ignoramos */ }
                onCompleted?.Invoke();
            });
        }

        /// <summary>
        /// Muestra el elemento en la vista activa (zoom-fit) y opcionalmente
        /// lo selecciona.  Equivalente al comando "Ver elemento" de Revit.
        /// </summary>
        public void ShowElement(ElementId id, bool alsoSelect = true)
        {
            if (id == null || id == ElementId.InvalidElementId) return;
            _bus.Enqueue(app =>
            {
                var uidoc = app?.ActiveUIDocument;
                if (uidoc == null) return;
                try
                {
                    if (alsoSelect)
                        uidoc.Selection.SetElementIds(new[] { id });
                    uidoc.ShowElements(id);
                }
                catch { /* elemento ya no existe en el documento */ }
            });
        }

        /// <summary>Vacía la selección actual de Revit.</summary>
        public void Clear()
        {
            _bus.Enqueue(app =>
            {
                var uidoc = app?.ActiveUIDocument;
                if (uidoc == null) return;
                try { uidoc.Selection.SetElementIds(new List<ElementId>()); }
                catch { }
            });
        }
    }
}
