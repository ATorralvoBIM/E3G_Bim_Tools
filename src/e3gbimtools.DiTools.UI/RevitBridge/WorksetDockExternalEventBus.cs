// ============================================================================
//  WorksetDockExternalEventBus.cs
//  Bus de acciones Revit seguro para el dockable pane de visibilidad.
//
//  Un dockable pane es MODELESS: los clics de su UI ocurren fuera del contexto
//  de la API de Revit, así que abrir una Transaction directamente lanzaría:
//    "Starting a transaction from an external application running outside
//     of API context is not allowed".
//
//  Solución (mismo patrón que e3gbimtools.Calc.RevitBridge.CalcExternalEventBus):
//  encolar las acciones aquí y ejecutarlas dentro de IExternalEventHandler.Execute(),
//  que Revit invoca en contexto válido y en el hilo de UI (= hilo WPF).
// ============================================================================
using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace E3G.BimTools.DiTools.UI.RevitBridge;

public sealed class WorksetDockExternalEventBus : IExternalEventHandler, IDisposable
{
    private readonly Queue<Action<UIApplication>> _queue = new();
    private readonly object _gate = new();
    private ExternalEvent? _externalEvent;
    private bool _disposed;

    public WorksetDockExternalEventBus()
    {
        // Debe crearse en el hilo principal de Revit (contexto API válido):
        // p. ej. desde ApplicationInitialized.
        _externalEvent = ExternalEvent.Create(this);
    }

    /// <summary>
    /// Encola una acción para ejecutarse en contexto válido de la API de Revit.
    /// Puede llamarse desde cualquier hilo.
    /// </summary>
    public void Enqueue(Action<UIApplication> action)
    {
        if (_disposed || action is null) return;
        lock (_gate) { _queue.Enqueue(action); }
        try { _externalEvent?.Raise(); } catch { /* ignore */ }
    }

    // IExternalEventHandler — llamado por Revit en contexto válido.
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
            try { action(app); } catch { /* cada acción gestiona sus errores */ }
        }
    }

    public string GetName() => "e3gbimtools.DiTools.UI.WorksetDockExternalEventBus";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _externalEvent?.Dispose(); } catch { }
        _externalEvent = null;
    }
}
