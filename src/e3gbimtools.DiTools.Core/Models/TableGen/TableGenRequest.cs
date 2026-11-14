namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Parametros de una peticion de generacion de Drafting View (Sprint 8 — TableGen).
/// </summary>
public sealed class TableGenRequest
{
    /// <summary>Ruta completa al archivo .xlsx.</summary>
    public string FilePath { get; init; } = "";

    /// <summary>Nombre de la hoja de calculo.</summary>
    public string SheetName { get; init; } = "";

    /// <summary>
    /// Rango de celdas a leer, p. ej. "A1:J20".
    /// Null o vacío = todo el rango utilizado de la hoja.
    /// </summary>
    public string? RangeAddress { get; init; }

    /// <summary>
    /// Factor de escala: cuantos pies de Revit equivalen a 1 mm de Excel.
    /// Valor por defecto: 1/304.8 (1 mm = 1/304.8 ft).
    /// </summary>
    public double MmToFeet { get; init; } = 1.0 / 304.8;

    /// <summary>
    /// Nombre que se asignara a la nueva Drafting View.
    /// Null = se genera automaticamente como "TableGen_{fichero}_{hoja}".
    /// </summary>
    public string? ViewName { get; init; }

    /// <summary>
    /// Si ya existe una Drafting View con el mismo nombre, la elimina y
    /// regenera en lugar de añadir sufijo numerico (Sprint 9 — TableGen P2).
    /// </summary>
    public bool OverwriteExisting { get; init; } = false;
}
