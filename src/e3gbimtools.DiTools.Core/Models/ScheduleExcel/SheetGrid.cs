using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

/// <summary>
/// Rejilla para la vista previa de una tabla (antes de exportar) o de una
/// pestaña de Excel (al importar). Independiente de WPF y de Revit.
/// </summary>
public sealed class SheetGrid
{
    /// <summary>Pestaña de Excel (al importar) o nombre de la tabla.</summary>
    public string SheetName { get; set; } = "";
    public string ScheduleName { get; set; } = "";
    /// <summary>Anchos de columna en caracteres de Excel.</summary>
    public List<double> ColumnWidths { get; } = new();
    public List<GridRow> Rows { get; } = new();
    /// <summary>Filas totales de la fuente (puede haber mas que <see cref="Rows"/>).</summary>
    public int TotalRows { get; set; }
    public bool Truncated => TotalRows > Rows.Count;
}

public sealed class GridRow
{
    /// <summary>Fila de Excel (1-based); 0 si no viene de un Excel.</summary>
    public int ExcelRow { get; set; }
    public List<GridCell> Cells { get; } = new();
}

public sealed class GridCell
{
    /// <summary>Columna (1-based).</summary>
    public int Column { get; set; }
    public int ColSpan { get; set; } = 1;
    public string Text { get; set; } = "";
    /// <summary>Fondo RRGGBB o null.</summary>
    public string? Background { get; set; }
    /// <summary>Texto RRGGBB o null (negro).</summary>
    public string? Foreground { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public GridAlign Align { get; set; }
}

public enum GridAlign { Left, Center, Right }
