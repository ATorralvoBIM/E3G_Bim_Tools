using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.UI.Views.ScheduleExcel;

/// <summary>
/// Preferencias de «TableLink» en %APPDATA%\e3g\DiTools\ScheduleExcel\settings.json.
/// La carpeta de exportación se guarda por proyecto: la clave es el modelo
/// central (si es colaborativo), la ruta del archivo o, si no se ha guardado,
/// su título.
/// </summary>
public sealed class ScheduleExcelSettings
{
    public Dictionary<string, string> ProjectFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Editable { get; set; }
    public bool DatePrefix { get; set; }
    public bool AskPath { get; set; }
    public bool OpenAfter { get; set; } = true;
    public string? LastImportFolder { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "e3g", "DiTools", "ScheduleExcel", "settings.json");

    public static ScheduleExcelSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<ScheduleExcelSettings>(File.ReadAllText(FilePath));
                if (s is not null)
                {
                    s.ProjectFolders = new Dictionary<string, string>(s.ProjectFolders ?? new(), StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch { /* preferencias dañadas: se empieza de cero */ }
        return new ScheduleExcelSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* no es critico */ }
    }

    public string? GetFolder(string projectKey) =>
        ProjectFolders.TryGetValue(projectKey, out var f) && !string.IsNullOrWhiteSpace(f) ? f : null;

    public void SetFolder(string projectKey, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) ProjectFolders.Remove(projectKey);
        else ProjectFolders[projectKey] = folder.Trim();
    }

    /// <summary>Identificador estable del proyecto para guardar su carpeta.</summary>
    public static string ProjectKey(Document doc)
    {
        try
        {
            if (doc.IsWorkshared)
            {
                var central = doc.GetWorksharingCentralModelPath();
                if (central is not null)
                {
                    var p = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                    if (!string.IsNullOrWhiteSpace(p)) return p;
                }
            }
        }
        catch { /* sin central accesible */ }
        return !string.IsNullOrWhiteSpace(doc.PathName) ? doc.PathName : doc.Title;
    }
}
