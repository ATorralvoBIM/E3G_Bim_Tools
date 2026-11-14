// ============================================================================
//  CalcExternalEventBus.cs
//  Bus de acciones Revit seguro para ventanas no-modales.
//
//  Las ventanas WPF abiertas con Show() (no ShowDialog()) pierden el contexto
//  de la API de Revit cuando IExternalCommand.Execute() retorna.  Cualquier
//  Transaction abierta desde un handler WPF posterior lanzará:
//    "Starting a transaction from an external application running outside
//     of API context is not allowed".
//
//  Solución: encolar las acciones en este bus y ejecutarlas dentro de
//  IExternalEventHandler.Execute(), que Revit invoca en contexto válido.
//
//  Uso:
//    var bus = new CalcExternalEventBus();          // en contexto de Execute()
//    bus.Enqueue(app => { /* usa app, doc, etc. */ });
//    // Al cerrar: bus.Dispose();
// ============================================================================
using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace e3gbimtools.Calc.RevitBridge
{
    public sealed class CalcExternalEventBus : IExternalEventHandler, IDisposable
    {
        private readonly Queue<Action<UIApplication>> _queue
            = new Queue<Action<UIApplication>>();
        private readonly object _gate = new object();
        private ExternalEvent _externalEvent;
        private bool _disposed;

        public CalcExternalEventBus()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        /// <summary>
        /// Encola una acción para ejecutarse en contexto válido de la API de Revit.
        /// Puede llamarse desde cualquier hilo.
        /// </summary>
        public void Enqueue(Action<UIApplication> action)
        {
            if (_disposed) return;
            lock (_gate) { _queue.Enqueue(action); }
            try { _externalEvent?.Raise(); } catch { }
        }

        // IExternalEventHandler — llamado por Revit en contexto válido
        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication> action;
                lock (_gate)
                {
                    if (_queue.Count == 0) break;
                    action = _queue.Dequeue();
                }
                try { action(app); } catch { /* las acciones loguean sus propios errores */ }
            }
        }

        public string GetName() => "e3gbimtools.Calc.RevitBridge.CalcExternalEventBus";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _externalEvent?.Dispose(); } catch { }
            _externalEvent = null;
        }
    }
}
