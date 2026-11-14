using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Filter;

namespace E3G.BimTools.DiTools.Core.Services.Filter;

/// <summary>
/// Persiste plantillas OneFilter en disco como JSON.  Carpeta:
/// <c>%APPDATA%\e3g\DiTools\FilterTemplates\</c>.  Un fichero por plantilla.
///
/// Decisiones:
///  - Usa <see cref="System.Text.Json"/> (incluido en .NET 8) para evitar
///    dependencia de NuGet.
///  - Si una plantilla esta corrupta o cambia el esquema, <see cref="Load"/>
///    devuelve null y registra un warning en el log — la UI puede decidir.
///  - El nombre del fichero se sanea para evitar caracteres invalidos en
///    Windows.
/// </summary>
public sealed class FilterTemplateService : IFilterTemplateService
{
    private readonly ILogger _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string TemplatesFolder { get; }

    public FilterTemplateService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<FilterTemplateService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        TemplatesFolder = Path.Combine(appData, "e3g", "DiTools", "FilterTemplates");
        try { Directory.CreateDirectory(TemplatesFolder); }
        catch (Exception ex)
        {
            _logger.Warning("No se pudo crear la carpeta de plantillas '{0}': {1}", TemplatesFolder, ex.Message);
        }
    }

    public IReadOnlyList<string> ListTemplates()
    {
        try
        {
            if (!Directory.Exists(TemplatesFolder)) return Array.Empty<string>();
            return Directory
                .EnumerateFiles(TemplatesFolder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warning("ListTemplates fallo: {0}", ex.Message);
            return Array.Empty<string>();
        }
    }

    public void Save(string name, FilterDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nombre de plantilla vacio.", nameof(name));
        if (definition is null) throw new ArgumentNullException(nameof(definition));

        definition.Name = name;
        Directory.CreateDirectory(TemplatesFolder);
        var path = Path.Combine(TemplatesFolder, Sanitize(name) + ".json");
        var json = JsonSerializer.Serialize(definition, JsonOpts);
        File.WriteAllText(path, json);
        _logger.Information("Plantilla OneFilter guardada: '{0}' -> {1}", name, path);
    }

    public FilterDefinition? Load(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = Path.Combine(TemplatesFolder, Sanitize(name) + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var def = JsonSerializer.Deserialize<FilterDefinition>(json, JsonOpts);
            return def;
        }
        catch (Exception ex)
        {
            _logger.Warning("Plantilla '{0}' corrupta o ilegible: {1}", name, ex.Message);
            return null;
        }
    }

    public bool Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var path = Path.Combine(TemplatesFolder, Sanitize(name) + ".json");
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch (Exception ex)
        {
            _logger.Warning("Delete '{0}' fallo: {1}", name, ex.Message);
            return false;
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "_" : clean;
    }
}
