using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace E3G.BimTools.DiTools.UI.Hosting;

/// <summary>
/// Ejecuta trabajo que necesita la API de Revit desde una vista WPF.  Asi la
/// misma vista sirve en una ventana modal (llamada directa) y en un panel
/// acoplable (ExternalEvent), donde la API solo se puede usar en el hilo de
/// Revit y dentro de su contexto.
/// </summary>
public interface IRevitInvoker
{
    /// <summary>
    /// Ejecuta <paramref name="trabajo"/> en contexto de API.  Toda llamada a Revit
    /// debe ir dentro; lo que se haga despues del await ya no esta en ese contexto.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<UIApplication, T> trabajo);
}

/// <summary>Para ventanas modales abiertas desde un comando: ya se esta en contexto de API.</summary>
public sealed class DirectRevitInvoker : IRevitInvoker
{
    private readonly UIApplication _app;

    public DirectRevitInvoker(UIApplication app) => _app = app ?? throw new ArgumentNullException(nameof(app));

    public Task<T> InvokeAsync<T>(Func<UIApplication, T> trabajo)
    {
        try { return Task.FromResult(trabajo(_app)); }
        catch (Exception ex) { return Task.FromException<T>(ex); }
    }
}

/// <summary>
/// Para paneles acoplables: encola el trabajo y lanza un ExternalEvent.  Debe
/// crearse en contexto de API (p. ej. en OnStartup).  Las continuaciones se
/// ejecutan despues, en el hilo de la interfaz, fuera del contexto de API.
/// </summary>
public sealed class ExternalEventRevitInvoker : IRevitInvoker, IExternalEventHandler
{
    private readonly object _cerrojo = new();
    private readonly List<Pendiente> _cola = new();
    private readonly Action<UIApplication>? _alEjecutar;
    private readonly ExternalEvent _evento;

    /// <param name="alEjecutar">Se llama con la UIApplication antes de cada lote (p. ej. para registrarla).</param>
    public ExternalEventRevitInvoker(Action<UIApplication>? alEjecutar = null)
    {
        _alEjecutar = alEjecutar;
        _evento = ExternalEvent.Create(this);
    }

    public Task<T> InvokeAsync<T>(Func<UIApplication, T> trabajo)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendiente = new Pendiente(
            app =>
            {
                try { tcs.SetResult(trabajo(app)); }
                catch (Exception ex) { tcs.SetException(ex); }
            },
            ex => tcs.TrySetException(ex));

        lock (_cerrojo) _cola.Add(pendiente);

        var respuesta = _evento.Raise();
        if (respuesta == ExternalEventRequest.Denied || respuesta == ExternalEventRequest.TimedOut)
        {
            bool quitado;
            lock (_cerrojo) quitado = _cola.Remove(pendiente);
            if (quitado)
                pendiente.Fallar(new InvalidOperationException(
                    "Revit no puede atender la petición ahora. Cierra el comando o el diálogo que tengas abierto y vuelve a intentarlo."));
        }
        return tcs.Task;
    }

    public void Execute(UIApplication app)
    {
        List<Pendiente> lote;
        lock (_cerrojo)
        {
            lote = new List<Pendiente>(_cola);
            _cola.Clear();
        }

        try { _alEjecutar?.Invoke(app); }
        catch { /* no debe impedir el trabajo */ }

        foreach (var pendiente in lote) pendiente.Ejecutar(app);
    }

    public string GetName() => "e3gbimtools · vista acoplable";

    private sealed class Pendiente
    {
        public Pendiente(Action<UIApplication> ejecutar, Action<Exception> fallar)
        {
            Ejecutar = ejecutar;
            Fallar = fallar;
        }

        public Action<UIApplication> Ejecutar { get; }
        public Action<Exception> Fallar { get; }
    }
}
