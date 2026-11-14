using System;
using System.Threading.Tasks;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Core.Errors;

/// <summary>
/// Captura las excepciones no controladas (AppDomain.UnhandledException y
/// TaskScheduler.UnobservedTaskException) para que siempre queden loggeadas
/// y se muestre un dialogo amistoso en lugar de tumbar Revit.
///
/// La logica de mostrar el dialogo vive en el proyecto host (.DiTools) via
/// el delegate <see cref="ShowDialog"/>.  Asi este ensamblado (.Core) no
/// depende de WPF.
/// </summary>
public sealed class GlobalExceptionHandler
{
    private readonly ILogger _logger;

    /// <summary>
    /// Callback que muestra el dialogo.  Si es <c>null</c>, solo se loggea.
    /// </summary>
    public Action<Exception>? ShowDialog { get; set; }

    public GlobalExceptionHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<GlobalExceptionHandler>();
    }

    public void Attach()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
    }

    public void Detach()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTask;
    }

    public void Handle(Exception ex)
    {
        try
        {
            _logger.Error(ex, "Excepcion no controlada.");
        }
        catch { /* no debe propagar nunca */ }

        try
        {
            ShowDialog?.Invoke(ex);
        }
        catch (Exception dialogEx)
        {
            try { _logger.Error(dialogEx, "Fallo mostrando el dialogo amistoso."); }
            catch { }
        }
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) Handle(ex);
    }

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Handle(e.Exception);
        e.SetObserved();
    }
}
