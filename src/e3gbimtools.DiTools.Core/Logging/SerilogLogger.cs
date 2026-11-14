using System;
using System.Globalization;
using System.IO;
using System.Text;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Core.Logging;

/// <summary>
/// Logger minimo escrito a archivo, sin dependencias externas.
/// Reemplazo de Serilog para el Sprint 0; cuando NuGet funcione
/// volveremos a Serilog con rolling diario.
///
/// El nombre de la clase se mantiene como <c>SerilogLoggerFactory</c>
/// para no romper las referencias existentes.
/// </summary>
public sealed class SerilogLoggerFactory : ILoggerFactory, IDisposable
{
    private readonly object _gate = new();
    private readonly string _revitVersion;
    private bool _disposed;

    public SerilogLoggerFactory(string? revitVersion = null)
    {
        _revitVersion = revitVersion ?? "unknown";
        // Toca la carpeta para asegurar que existe y deja un mensaje de arranque.
        try { File.AppendAllText(LogFilePath, BuildLine("INF", "Logger", "Logger inicializado.", null)); }
        catch { /* sin loguear si no se puede */ }
    }

    private static string LogFilePath
    {
        get
        {
            var folder = ProjectInfo.LogsFolder;
            var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return Path.Combine(folder, $"dt-{today}.log");
        }
    }

    public ILogger CreateLogger(string category) => new FileLogger(this, category);
    public ILogger CreateLogger<T>() => CreateLogger(typeof(T).FullName ?? typeof(T).Name);

    public void Dispose() => _disposed = true;

    internal void Write(string level, string category, string message, Exception? ex)
    {
        if (_disposed) return;
        try
        {
            var line = BuildLine(level, category, message, ex);
            lock (_gate)
            {
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch { /* no hace nada si no se puede escribir */ }
    }

    private static string BuildLine(string level, string category, string message, Exception? ex)
    {
        var sb = new StringBuilder(256);
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append(" [").Append(level).Append("] ");
        sb.Append('(').Append(category).Append(") ");
        sb.AppendLine(message);
        if (ex is not null) sb.AppendLine(ex.ToString());
        return sb.ToString();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly SerilogLoggerFactory _factory;
        private readonly string _category;
        public FileLogger(SerilogLoggerFactory factory, string category) { _factory = factory; _category = category; }

        public void Information(string message, params object[] args) => _factory.Write("INF", _category, Format(message, args), null);
        public void Warning(string message, params object[] args)     => _factory.Write("WRN", _category, Format(message, args), null);
        public void Debug(string message, params object[] args)       => _factory.Write("DBG", _category, Format(message, args), null);
        public void Error(Exception? ex, string message, params object[] args) => _factory.Write("ERR", _category, Format(message, args), ex);

        private static string Format(string message, object[] args)
        {
            if (args is null || args.Length == 0) return message;
            try { return string.Format(CultureInfo.InvariantCulture, message, args); }
            catch { return message; }
        }
    }
}
