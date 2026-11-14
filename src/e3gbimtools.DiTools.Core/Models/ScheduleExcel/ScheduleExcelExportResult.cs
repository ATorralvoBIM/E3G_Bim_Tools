using System;
using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

/// <summary>Resultado de exportar una o varias tablas de planificacion a Excel.</summary>
public sealed class ScheduleExcelExportResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    /// <summary>Nombre de la tabla, o "N tablas".</summary>
    public string? ScheduleName { get; init; }

    /// <summary>Pestañas de tabla escritas.</summary>
    public int SheetCount { get; init; }
    /// <summary>Filas escritas en el cuerpo (todas las pestañas).</summary>
    public int RowCount { get; init; }
    public int ColumnCount { get; init; }

    /// <summary>Campos omitidos en formato editable ("Tabla: Campo").</summary>
    public IReadOnlyList<string> SkippedFields { get; init; } = Array.Empty<string>();

    public string? ErrorMessage { get; init; }
}
