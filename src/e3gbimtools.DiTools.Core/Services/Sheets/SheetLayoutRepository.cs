using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sheets;

namespace E3G.BimTools.DiTools.Core.Services.Sheets;

/// <summary>
/// Carga y guarda los layouts de SheetGen como JSON en
/// %APPDATA%\e3g\DiTools\SheetLayouts\ (Sprint 6).
/// Thread-safe para lecturas concurrentes; escribe sincronamente.
/// </summary>
public sealed class SheetLayoutRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _directory;
    private readonly ILogger _logger;

    public SheetLayoutRepository(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<SheetLayoutRepository>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));

        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "e3g", "DiTools", "SheetLayouts");
    }

    // Para tests: permite inyectar directorio custom.
    internal SheetLayoutRepository(ILoggerFactory loggerFactory, string directory)
    {
        _logger    = loggerFactory.CreateLogger<SheetLayoutRepository>();
        _directory = directory;
    }

    public IList<SheetLayoutDefinition> GetAll()
    {
        EnsureDirectory();
        var result = new List<SheetLayoutDefinition>();

        foreach (var file in Directory.GetFiles(_directory, "*.json"))
        {
            try
            {
                var json   = File.ReadAllText(file);
                var layout = JsonSerializer.Deserialize<SheetLayoutDefinition>(json, JsonOptions);
                if (layout is not null) result.Add(layout);
            }
            catch (Exception ex)
            {
                _logger.Warning("SheetLayoutRepository: no se pudo leer '{File}': {Message}", file, ex.Message);
            }
        }

        return result;
    }

    public void Save(SheetLayoutDefinition layout)
    {
        if (layout is null) throw new ArgumentNullException(nameof(layout));
        EnsureDirectory();

        var path = FilePath(layout.Id);
        var json = JsonSerializer.Serialize(layout, JsonOptions);
        File.WriteAllText(path, json);
        _logger.Information("SheetLayoutRepository: layout '{Name}' guardado en '{Path}'.", layout.Name, path);
    }

    public void Delete(Guid layoutId)
    {
        var path = FilePath(layoutId);
        if (!File.Exists(path)) return;
        File.Delete(path);
        _logger.Information("SheetLayoutRepository: layout {Id} eliminado.", layoutId);
    }

    // ------------------------------------------------------------------ //

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
    }

    private string FilePath(Guid id) =>
        Path.Combine(_directory, $"{id:N}.json");
}
