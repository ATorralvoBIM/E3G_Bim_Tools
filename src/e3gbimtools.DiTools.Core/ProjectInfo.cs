using System;
using System.IO;
using System.Reflection;

namespace E3G.BimTools.DiTools.Core;

/// <summary>
/// Constantes y rutas comunes a todo el modulo DiTools.
/// </summary>
public static class ProjectInfo
{
    /// <summary>Nombre publico de la suite.</summary>
    public const string ProductName = "e3gbimtools";

    /// <summary>Nombre del modulo (siempre "DiTools" para este ensamblado).</summary>
    public const string ModuleName = "DiTools";

    /// <summary>Vendor / fabricante.</summary>
    public const string VendorName = "E3G";

    /// <summary>
    /// Carpeta de datos del usuario: %APPDATA%\e3g\DiTools\
    /// Se crea si no existe.
    /// </summary>
    public static string AppDataRoot
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "e3g",
                ModuleName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Carpeta de logs: %APPDATA%\e3g\DiTools\logs\</summary>
    public static string LogsFolder
    {
        get
        {
            var path = Path.Combine(AppDataRoot, "logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Version informacional del ensamblado (semver + tag).</summary>
    public static string AssemblyVersion =>
        typeof(ProjectInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(ProjectInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
