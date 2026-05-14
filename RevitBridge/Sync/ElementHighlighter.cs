// ============================================================================
//  ElementHighlighter.cs
//  Resaltado temporal de elementos en la vista activa de Revit, con
//  auto-revert tras un timeout.
//
//  Estrategia:
//    Para mantener el ciclo "hover → highlight 1.5 s → revert" ligero, el
//    highlight se hace con OverrideGraphicSettings dentro de una Transaction.
//    El revert se programa con un Timer en el thread de UI; cuando salta,
//    encola la transacción inversa por el ExternalEventBus.
//
//  Por qué no TemporaryGraphicsManager:
//    TemporaryGraphicsManager dibuja "decoración" 3D libre, no override de
//    gráficos sobre un elemento existente.  Para resaltar una pipe del
//    sistema lo que queremos es cambiarle color/grosor a su línea de
//    proyección, eso es OverrideGraphicSettings.
//
//  Restricciones:
//    - Override válido sólo en la vista activa.
//    - Cada highlight abre y cierra una Transaction muy ligera; el motor de
//      undo se ensucia pero al ser elementos de Override no se acumula en
//      el modelo.  Si molesta, encerrar todo en un TransactionGroup que se
//      cancele al final (no aparece en undo stack).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.Saneamiento.RevitBridge.Sync
{
    public sealed class ElementHighlighter : IDisposable
    {
        private readonly ExternalEventBus _bus;
        private readonly Color _highlightColor;
        private readonly int _highlightWeight;
        private readonly Dictionary<long, DispatcherTimer> _pendingReverts
            = new Dictionary<long, DispatcherTimer>();
        private bool _disposed;

        public ElementHighlighter(ExternalEventBus bus,
                                  Color highlightColor = null,
                                  int highlightWeight = 8)
        {
            _bus = bus;
            _highlightColor = highlightColor ?? new Color(255, 200, 0); // ámbar
            _highlightWeight = highlightWeight;
        }

        /// <summary>
        /// Resalta un elemento durante <paramref name="durationMs"/> ms en la
        /// vista activa.  Si ya estaba resaltado, reinicia el contador.
        /// </summary>
        public void HighlightOnce(ElementId id, int durationMs = 1500)
        {
            if (_disposed || id == null || id == ElementId.InvalidElementId) return;
            long key = id.Value;

            // Cancelar cualquier revert pendiente para no quitar el highlight
            // demasiado pronto si el usuario sigue hovering.
            if (_pendingReverts.TryGetValue(key, out var oldTimer))
            {
                oldTimer.Stop();
                _pendingReverts.Remove(key);
            }

            // Aplicar override.
            _bus.Enqueue(app => ApplyOverride(app, id));

            // Programar el revert.
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs),
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _pendingReverts.Remove(key);
                _bus.Enqueue(app => ClearOverride(app, id));
            };
            _pendingReverts[key] = timer;
            timer.Start();
        }

        /// <summary>Cancela cualquier highlight pendiente para un elemento.</summary>
        public void Cancel(ElementId id)
        {
            if (id == null) return;
            long key = id.Value;
            if (_pendingReverts.TryGetValue(key, out var t))
            {
                t.Stop();
                _pendingReverts.Remove(key);
            }
            _bus.Enqueue(app => ClearOverride(app, id));
        }

        /// <summary>Limpia overrides aún activos y para los timers.</summary>
        public void ClearAll()
        {
            foreach (var t in _pendingReverts.Values) t.Stop();
            var keys = new List<long>(_pendingReverts.Keys);
            _pendingReverts.Clear();
            foreach (var k in keys)
                _bus.Enqueue(app => ClearOverride(app, new ElementId(k)));
        }

        private void ApplyOverride(UIApplication app, ElementId id)
        {
            var doc = app?.ActiveUIDocument?.Document;
            var view = app?.ActiveUIDocument?.ActiveView;
            if (doc == null || view == null) return;
            try
            {
                using (var tx = new Transaction(doc, "MEPCalc highlight"))
                {
                    tx.Start();
                    var ogs = new OverrideGraphicSettings();

                    // Líneas de proyección y de corte muy gruesas en color
                    // ámbar; cubre vistas 2D (planta, esquema) y 3D wireframe.
                    ogs.SetProjectionLineColor(_highlightColor);
                    ogs.SetProjectionLineWeight(_highlightWeight);
                    ogs.SetCutLineColor(_highlightColor);
                    ogs.SetCutLineWeight(_highlightWeight);

                    // Patrón de superficie sólido en el mismo color para que
                    // el override sea visible también en vistas 3D sombreadas
                    // y en vistas con realistic display style.
                    var solidFill = GetSolidFillPatternId(doc);
                    if (solidFill != ElementId.InvalidElementId)
                    {
                        ogs.SetSurfaceForegroundPatternId(solidFill);
                        ogs.SetSurfaceForegroundPatternColor(_highlightColor);
                        ogs.SetSurfaceForegroundPatternVisible(true);
                        ogs.SetCutForegroundPatternId(solidFill);
                        ogs.SetCutForegroundPatternColor(_highlightColor);
                        ogs.SetCutForegroundPatternVisible(true);
                    }

                    // Garantizar que el elemento NO está en halftone aunque
                    // el filter de la vista lo haya marcado.
                    ogs.SetHalftone(false);

                    view.SetElementOverrides(id, ogs);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[ElementHighlighter] ApplyOverride falló: " + ex.Message);
            }
        }

        /// <summary>
        /// Resuelve el ElementId del patrón "solid fill" del documento, que
        /// usamos para teñir la superficie completa del elemento.
        /// </summary>
        private static ElementId GetSolidFillPatternId(Document doc)
        {
            try
            {
                var pattern = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(p => p.GetFillPattern().IsSolidFill);
                return pattern?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private void ClearOverride(UIApplication app, ElementId id)
        {
            var doc = app?.ActiveUIDocument?.Document;
            var view = app?.ActiveUIDocument?.ActiveView;
            if (doc == null || view == null) return;
            try
            {
                using (var tx = new Transaction(doc, "MEPCalc unhighlight"))
                {
                    tx.Start();
                    view.SetElementOverrides(id, new OverrideGraphicSettings());
                    tx.Commit();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _disposed = true;
            ClearAll();
        }
    }
}
