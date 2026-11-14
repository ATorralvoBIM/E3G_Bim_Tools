using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Bootstrap;

/// <summary>
/// Implementacion de <see cref="IRevitUiDispatcher"/> sobre <see cref="ExternalEvent"/>.
/// Permite a codigo en hilos de fondo (async/await) volver al hilo de UI de Revit
/// para tocar el modelo.
/// </summary>
internal sealed class RevitUiDispatcher : IRevitUiDispatcher, IExternalEventHandler
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly Lazy<ExternalEvent> _externalEvent;

    public RevitUiDispatcher()
    {
        _externalEvent = new Lazy<ExternalEvent>(() => ExternalEvent.Create(this));
    }

    public Task InvokeAsync(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        _queue.Enqueue(() =>
        {
            try { action(); tcs.SetResult(true); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        _externalEvent.Value.Raise();
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        _queue.Enqueue(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        _externalEvent.Value.Raise();
        return tcs.Task;
    }

    // ====== IExternalEventHandler =================================
    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var action))
        {
            action();
        }
    }

    public string GetName() => "DiTools UI Dispatcher";
}
