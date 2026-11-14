using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.ViewSheets;

namespace E3G.BimTools.DiTools.Core.Services.ViewSheets;

/// <summary>
/// Carga y guarda la configuración de instalaciones del módulo
/// "View &amp; Sheet Manager" como JSON en
/// <c>%APPDATA%\e3g\DiTools\ViewSheetManager\instalaciones.json</c>.
/// Mismo patrón que <c>SheetLayoutRepository</c>.
/// </summary>
public sealed class InstalacionConfigRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private const string FileName = "instalaciones.json";
    private const string PrefsFileName = "prefs.json";

    private readonly string _directory;
    private readonly ILogger _logger;

    public InstalacionConfigRepository(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<InstalacionConfigRepository>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));

        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "e3g", "DiTools", "ViewSheetManager");
    }

    // Para tests: permite inyectar directorio custom.
    internal InstalacionConfigRepository(ILoggerFactory loggerFactory, string directory)
    {
        _logger = loggerFactory.CreateLogger<InstalacionConfigRepository>();
        _directory = directory;
    }

    /// <summary>
    /// Devuelve la lista guardada.  Si no hay archivo (primera ejecución) o falla
    /// la lectura, devuelve <see cref="InstalacionConfig.Defaults"/>.
    /// </summary>
    public List<InstalacionConfig> Load()
    {
        var path = FilePath();
        if (!File.Exists(path))
            return InstalacionConfig.Defaults();

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<InstalacionConfig>>(json, JsonOptions);
            return list is { Count: > 0 } ? list : InstalacionConfig.Defaults();
        }
        catch (Exception ex)
        {
            _logger.Warning("InstalacionConfigRepository: no se pudo leer '{Path}': {Message}", path, ex.Message);
            return InstalacionConfig.Defaults();
        }
    }

    public void Save(IReadOnlyList<InstalacionConfig> instalaciones)
    {
        if (instalaciones is null) throw new ArgumentNullException(nameof(instalaciones));
        EnsureDirectory();

        var path = FilePath();
        var json = JsonSerializer.Serialize(instalaciones, JsonOptions);
        File.WriteAllText(path, json);
        _logger.Information("InstalacionConfigRepository: {Count} instalaciones guardadas en '{Path}'.",
            instalaciones.Count, path);
    }

    /// <summary>Carga las preferencias del panel; devuelve valores por defecto si no hay archivo.</summary>
    public ViewSheetPrefs LoadPrefs()
    {
        var path = Path.Combine(_directory, PrefsFileName);
        if (!File.Exists(path)) return new ViewSheetPrefs();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ViewSheetPrefs>(json, JsonOptions) ?? new ViewSheetPrefs();
        }
        catch (Exception ex)
        {
            _logger.Warning("InstalacionConfigRepository: no se pudieron leer prefs '{Path}': {Message}", path, ex.Message);
            return new ViewSheetPrefs();
        }
    }

    public void SavePrefs(ViewSheetPrefs prefs)
    {
        if (prefs is null) throw new ArgumentNullException(nameof(prefs));
        EnsureDirectory();
        var path = Path.Combine(_directory, PrefsFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(prefs, JsonOptions));
        _logger.Information("InstalacionConfigRepository: preferencias guardadas en '{Path}'.", path);
    }

    // ------------------------------------------------------------------ //

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
    }

    private string FilePath() => Path.Combine(_directory, FileName);
}
