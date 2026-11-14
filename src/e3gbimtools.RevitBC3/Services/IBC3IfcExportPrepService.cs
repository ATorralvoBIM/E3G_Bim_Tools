using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using E3G.BimTools.RevitBC3.Mapping;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Resultado de la preparacion de exportacion IFC.
/// </summary>
public sealed class BC3IfcExportPrepResult
{
    /// <summary>Numero de elementos a los que se escribieron los parametros BC3.</summary>
    public int ElementsUpdated { get; set; }

    /// <summary>Numero de categorias Revit procesadas.</summary>
    public int CategoriesProcessed { get; set; }

    /// <summary>Numero de parametros compartidos creados (0 si ya existian).</summary>
    public int ParametersCreated { get; set; }

    /// <summary>True si la operacion termino sin errores criticos.</summary>
    public bool Success { get; set; }

    /// <summary>Mensaje de error si Success=false.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Advertencias no criticas (p.ej. elementos sin GUID fiable).</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Escribe parametros compartidos de BC3 en los elementos Revit matcheados,
/// de forma que al exportar a IFC desde Revit, esos parametros aparezcan
/// como propiedades IFC en el modelo.
///
/// Parametros escritos (grupo "E3G_Pset_BC3"):
///   E3G_BC3_Code     (texto)   — Codigo de partida BC3 asignado al elemento.
///   E3G_BC3_Chapter  (texto)   — Codigo del capitulo.
///   E3G_BC3_Quantity (numero)  — Cantidad aportada por el elemento.
///   E3G_BC3_Unit     (texto)   — Unidad de la partida (m, m2, m3, ud...).
///
/// Requisito: Revit debe tener un archivo de parametros compartidos configurado,
/// o se crea uno automaticamente en %APPDATA%\e3g\BC3\E3G_BC3_Params.spf.
/// </summary>
public interface IBC3IfcExportPrepService
{
    /// <summary>
    /// Escribe los parametros BC3 en todos los elementos que contribuyen a mediciones.
    /// </summary>
    /// <param name="doc">Documento Revit activo.</param>
    /// <param name="app">Aplicacion Revit (necesaria para gestionar el .spf).</param>
    /// <param name="measurements">Mediciones con contribuciones de elementos.</param>
    /// <param name="rules">Reglas de mapeo activas (para resolver capitulo/unidad).</param>
    BC3IfcExportPrepResult WriteToModel(
        Document doc,
        Application app,
        IReadOnlyList<MeasurementResult> measurements,
        IReadOnlyList<MappingRule> rules);

    /// <summary>
    /// Limpia los valores de los parametros BC3 de todos los elementos del modelo.
    /// Util para deshacer la preparacion antes de exportar a IFC sin datos BC3.
    /// </summary>
    BC3IfcExportPrepResult ClearFromModel(Document doc);
}
