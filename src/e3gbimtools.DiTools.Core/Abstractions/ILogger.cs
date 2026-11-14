using System;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Abstraccion minima de logging para el modulo.  La implementacion concreta
/// (basada en Serilog) escribe en <c>%APPDATA%\e3g\DiTools\logs\</c> con
/// rolling diario.
/// </summary>
public interface ILogger
{
    void Information(string message, params object[] args);
    void Warning(string message, params object[] args);
    void Error(Exception? ex, string message, params object[] args);
    void Debug(string message, params object[] args);
}

/// <summary>
/// Crea instancias de <see cref="ILogger"/> con contexto (nombre del
/// componente que loggea) para que los mensajes salgan etiquetados.
/// </summary>
public interface ILoggerFactory
{
    ILogger CreateLogger(string category);
    ILogger CreateLogger<T>();
}
