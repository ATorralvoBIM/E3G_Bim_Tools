using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.BimTools.DiTools.Bootstrap;

/// <summary>
/// Carga el modulo Saneamiento (<c>e3gbimtools.Saneamiento.dll</c>, junto a esta
/// DLL) por reflexion e inicializa sus paneles acoplables y eventos.
///
/// Por que reflexion y no una referencia de proyecto: DiTools compila net8 contra
/// la API de Revit 2025 (y corre tambien en 2026), mientras que Saneamiento se
/// compila por version (net10 contra la API 2026 para Revit 2026, donde los enums
/// son Int64).  Una DLL net10 no se puede referenciar desde net8, pero si cargar
/// en el proceso de Revit 2026 (.NET 10).  Si la DLL falta o no carga (p. ej. una
/// build net10 en Revit 2025) el resto de DiTools arranca igual, sin el panel.
///
/// Los botones de la cinta apuntan a comandos PROXY de DiTools que instancian el
/// comando real desde el ensamblado ya cargado aqui, asi se garantiza que usan el
/// mismo estado estatico (sesion, ExternalEvent) que los paneles acoplables.
/// </summary>
internal static class SaneamientoBridge
{
    public const string AssemblyFileName = "e3gbimtools.Saneamiento.dll";
    private const string AppTypeName = "E3G.Saneamiento.App";

    private static Assembly? _assembly;

    /// <summary>true si el modulo se cargo e inicializo correctamente.</summary>
    public static bool IsAvailable => _assembly is not null;

    /// <summary>Error de la ultima carga (para el aviso al pulsar un boton).</summary>
    public static string? LoadError { get; private set; }

    public static bool TryInitialize(UIControlledApplication app)
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(SaneamientoBridge).Assembly.Location) ?? string.Empty;
            var path = Path.Combine(dir, AssemblyFileName);
            if (!File.Exists(path))
            {
                LoadError = $"No se encuentra {AssemblyFileName} en {dir}.";
                return false;
            }

            var asm = Assembly.LoadFrom(path);
            var appType = asm.GetType(AppTypeName, throwOnError: true)!;
            var init = appType.GetMethod("InicializarModulo", BindingFlags.Public | BindingFlags.Static)
                       ?? throw new MissingMethodException(AppTypeName, "InicializarModulo");
            init.Invoke(null, new object[] { app });

            _assembly = asm;
            LoadError = null;
            return true;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
            LoadError = inner.GetType().Name + ": " + inner.Message;
            return false;
        }
    }

    /// <summary>
    /// Pone una vista de DiTools como pagina de la barra lateral del panel MEP
    /// (<c>E3G.Saneamiento.App.RegistrarPaginaExterna</c>).  <paramref name="crear"/>
    /// devuelve el control WPF; el panel lo crea la primera vez que se pulsa el boton.
    /// La vista debe hablar con Revit por ExternalEvent (el panel no es modal).
    /// </summary>
    /// <returns>true si el panel la acepto; si no, la herramienta sigue en la cinta.</returns>
    public static bool RegistrarPagina(string clave, Func<object> crear)
    {
        if (_assembly is null) return false;
        try
        {
            var metodo = _assembly.GetType(AppTypeName)?.GetMethod(
                "RegistrarPaginaExterna", BindingFlags.Public | BindingFlags.Static);
            if (metodo is null) return false;
            metodo.Invoke(null, new object[] { clave, crear });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Ejecuta un IExternalCommand del modulo Saneamiento por nombre de tipo.</summary>
    public static Result Execute(
        string commandTypeName,
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        if (_assembly is null)
        {
            TaskDialog.Show("Saneamiento",
                "El modulo Saneamiento no esta cargado.\n\n" + (LoadError ?? "Motivo desconocido."));
            return Result.Cancelled;
        }

        var type = _assembly.GetType(commandTypeName, throwOnError: true)!;
        var cmd = (IExternalCommand)Activator.CreateInstance(type)!;
        return cmd.Execute(commandData, ref message, elements);
    }
}
