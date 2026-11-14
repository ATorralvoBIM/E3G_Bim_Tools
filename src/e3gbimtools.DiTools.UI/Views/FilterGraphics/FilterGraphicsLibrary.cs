using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>Preset disponible en el desplegable del panel.</summary>
public sealed class FilterGraphicsPresetFile
{
    public FilterGraphicsPresetFile(string path, bool fromLibrary)
    {
        Path = path;
        FromLibrary = fromLibrary;
    }

    public string Path { get; }
    public bool   FromLibrary { get; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Display => FromLibrary ? $"{Name}  (biblioteca)" : $"{Name}  (local)";
}

/// <summary>
/// Presets de grafismo de filtros: carpeta local del usuario y, opcionalmente,
/// una carpeta de biblioteca compartida (p. ej. en red) con los estándar E3G.
/// La ruta de la biblioteca se guarda en <c>settings.json</c> de la carpeta local.
/// </summary>
public static class FilterGraphicsLibrary
{
    public static string LocalFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "e3g", "DiTools", "FilterGraphics");

    private static string SettingsPath => Path.Combine(LocalFolder, "settings.json");

    private sealed class Settings
    {
        public string? LibraryFolder { get; set; }
    }

    public static string? LibraryFolder
    {
        get
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                return string.IsNullOrWhiteSpace(s?.LibraryFolder) ? null : s!.LibraryFolder;
            }
            catch { return null; }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(LocalFolder);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                    new Settings { LibraryFolder = value }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }
    }

    public static string EnsureLocalFolder()
    {
        try { Directory.CreateDirectory(LocalFolder); } catch { }
        return LocalFolder;
    }

    /// <summary>Presets de la biblioteca (primero) y locales. Excluye settings.json.</summary>
    public static IReadOnlyList<FilterGraphicsPresetFile> ListPresets()
    {
        var list = new List<FilterGraphicsPresetFile>();
        var lib = LibraryFolder;
        if (lib is not null) list.AddRange(Enumerate(lib, fromLibrary: true));
        if (!string.Equals(lib, LocalFolder, StringComparison.OrdinalIgnoreCase))
            list.AddRange(Enumerate(LocalFolder, fromLibrary: false));
        return list;
    }

    private static IEnumerable<FilterGraphicsPresetFile> Enumerate(string folder, bool fromLibrary)
    {
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<FilterGraphicsPresetFile>();
            return Directory.EnumerateFiles(folder, "*.json")
                .Where(p => !string.Equals(Path.GetFileName(p), "settings.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new FilterGraphicsPresetFile(p, fromLibrary))
                .ToList();
        }
        catch { return Array.Empty<FilterGraphicsPresetFile>(); }
    }
}
