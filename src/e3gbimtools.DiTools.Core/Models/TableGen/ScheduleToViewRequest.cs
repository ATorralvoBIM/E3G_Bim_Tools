using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Peticion para convertir un ViewSchedule de Revit en una Drafting View
/// (Sprint 9 — TableGen P2).
/// </summary>
public sealed class ScheduleToViewRequest
{
    /// <summary>Id del ViewSchedule origen.</summary>
    public ElementId ScheduleId { get; init; } = ElementId.InvalidElementId;

    /// <summary>
    /// Nombre que se asignara a la nueva Drafting View.
    /// Null = usa el nombre del schedule con prefijo "TableGen_".
    /// </summary>
    public string? ViewName { get; init; }

    /// <summary>
    /// Factor de escala mm→ft. Por defecto 1/304.8 (1 mm = 1/304.8 ft).
    /// </summary>
    public double MmToFeet { get; init; } = 1.0 / 304.8;

    /// <summary>
    /// Multiplicador sobre los anchos de columna del schedule.
    /// 1.0 = usa los anchos reales del schedule (convertidos de ft a mm).
    /// Valores mayores agrandan las columnas.
    /// </summary>
    public double ColumnWidthMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Altura de fila para filas de cabecera (mm).
    /// Por defecto 6 mm.
    /// </summary>
    public double HeaderRowHeightMm { get; init; } = 6.0;

    /// <summary>
    /// Altura de fila para filas de datos (mm).
    /// Por defecto 5 mm.
    /// </summary>
    public double BodyRowHeightMm { get; init; } = 5.0;

    /// <summary>
    /// Si ya existe una Drafting View con el mismo nombre, la elimina antes
    /// de regenerar.
    /// </summary>
    public bool OverwriteExisting { get; init; } = false;
}
