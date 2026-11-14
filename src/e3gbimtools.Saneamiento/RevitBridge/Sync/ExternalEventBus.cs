// ============================================================================
//  ExternalEventBus.cs
//  Cola unificada de acciones que necesitan ejecutarse dentro de la API de
//  Revit (transacciones, override de gráficos, modificar selección desde un
//  contexto no-modal).  Cualquier acción que toque la API se encola con
//  Enqueue(...) y se ejecuta en el contexto correcto a través de un único
//  IExternalEventHandler.
//
//  Por qué hace falta:
//    Una ventana WPF no-modal corre en el hilo de UI de Revit, pero no
//    SIEMPRE está dentro de una "valid Revit API context" — sólo lo está
//    cuando Revit le cede el turno explícitamente.  ExternalEvent.Raise()
//    pide ese turno al motor de Revit y dispara Execute() cuando llega.
//
//  Diseño:
//    - Una sola instancia (singleton por ventana de cálculo).
//    - Cola de Action<UIApplication> que se vacía en cada Execute.
//    - Thread-safe vía lock; las acciones se ejecutan en orden FIFO.
// ============================================================================
using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace E3G.Saneamiento.RevitBridge.Sync
{
    public sealed class ExternalEventBus : IExternalEventHandler, IDisposable
    {
        private readonly object _gate = new object();
        private readonly Queue<Action<UIApplication>> _queue
            = new Queue<Action<UIApplication>>();
        private ExternalEvent _externalEvent;
        private bool _disposed;

        public ExternalEventBus()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        /// <summary>
        /// Encola una acción para que se ejecute en el próximo turno de Revit.
        /// Devuelve inmediatamente; la acción corre asíncrona.
        /// </summary>
        public void Enqueue(Action<UIApplication> action)
        {
            if (action == null || _disposed) return;
            lock (_gate) { _queue.Enqueue(action); }
            try { _externalEvent?.Raise(); }
            catch { /* Revit puede rechazar el Raise; reintentaremos en el siguiente Enqueue */ }
        }

        /// <summary>Implementación de IExternalEventHandler — la llama Revit.</summary>
        public void Execute(UIApplication app)
        {
            Action<UIApplication>[] batch;
            lock (_gate)
            {
                if (_queue.Count == 0) return;
                batch = _queue.ToArray();
                _queue.Clear();
            }
            foreach (var act in batch)
            {
                try { act(app); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[ExternalEventBus] acción falló: " + ex);
                }
            }
        }

        public string GetName() => "E3G.MEPCalc.ExternalEventBus";

        public void Dispose()
        {
            _disposed = true;
            lock (_gate) { _queue.Clear(); }
            _externalEvent = null;   // no hay API explícita de "dispose"
        }
    }
}
