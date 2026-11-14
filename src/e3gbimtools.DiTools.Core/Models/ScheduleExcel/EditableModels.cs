using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

/// <summary>Tipo de valor de una columna editable.</summary>
public enum EditableKind
{
    Text,
    Integer,
    Number,
    YesNo,
    /// <summary>Referencia a otro elemento (nivel, material…): se muestra el nombre, solo lectura.</summary>
    ElementRef,
}

/// <summary>Columna del formato editable (un campo de la tabla).</summary>
public sealed class EditableColumn
{
    /// <summary>Clave estable "parametro:I|T" (I = ejemplar, T = tipo).</summary>
    public string Key { get; set; } = "";
    public string Heading { get; set; } = "";
    /// <summary>Id del parametro (negativo para los de Revit).</summary>
    public long ParameterId { get; set; }
    /// <summary>Parametro de tipo: al cambiarlo afecta a todos los ejemplares del tipo.</summary>
    public bool IsType { get; set; }
    public EditableKind Kind { get; set; }
    public bool Editable { get; set; }
    /// <summary>ForgeTypeId de la unidad de visualizacion (solo Number).</summary>
    public string? UnitTypeId { get; set; }
    /// <summary>ForgeTypeId del tipo de dato (solo Number).</summary>
    public string? SpecTypeId { get; set; }
    public int Decimals { get; set; }
}

/// <summary>Fila del formato editable: un elemento.</summary>
public sealed class EditableRow
{
    public long ElementId { get; set; }
    /// <summary>Fila de Excel de la que se leyo (0 al exportar).</summary>
    public int ExcelRow { get; set; }
    /// <summary>Valores por columna: string, double, bool o null.</summary>
    public List<object?> Values { get; } = new();
}

/// <summary>Tabla en formato editable (una fila por elemento).</summary>
public sealed class ScheduleEditableData
{
    public string Name { get; set; } = "";
    public string? ScheduleUniqueId { get; set; }
    public string? DocumentTitle { get; set; }
    public List<EditableColumn> Columns { get; } = new();
    public List<EditableRow> Rows { get; } = new();
    /// <summary>Campos que no se pueden exportar en modo editable (calculados, recuento…).</summary>
    public List<string> SkippedFields { get; } = new();
}

/// <summary>Una pestaña (tabla) leida de un Excel editable.</summary>
public sealed class ParsedEditableWorkbook
{
    public string SheetName { get; set; } = "";
    public Dictionary<string, string> Meta { get; } = new();
    public List<EditableColumn> Columns { get; } = new();
    /// <summary>Columna de Excel (1-based) de cada columna de <see cref="Columns"/>; 0 si falta.</summary>
    public List<int> ColumnExcelIndex { get; } = new();
    /// <summary>Columna de Excel del ElementId.</summary>
    public int IdExcelColumn { get; set; }
    public List<EditableRow> Rows { get; } = new();
    public List<string> Warnings { get; } = new();

    public string? ScheduleName => Meta.TryGetValue("ScheduleName", out var v) ? v : null;
    public string? ScheduleUniqueId => Meta.TryGetValue("ScheduleUniqueId", out var v) ? v : null;
    public string? DocumentTitle => Meta.TryGetValue("Document", out var v) ? v : null;
    public string? ExportedAt => Meta.TryGetValue("ExportedAt", out var v) ? v : null;
}

/// <summary>Fila de una pestaña «como en Revit» leida para importar.</summary>
public sealed class PresentationRow
{
    public int ExcelRow { get; set; }
    /// <summary>Texto que identifica la fila (grupos + 1ª columna) para la vista previa.</summary>
    public string Label { get; set; } = "";
    /// <summary>Elementos de la fila; vacio si no se pudieron identificar al exportar.</summary>
    public List<long> ElementIds { get; } = new();
    public bool Ambiguous { get; set; }
    /// <summary>Texto original de cada columna al exportar (para saber que ha cambiado el usuario).</summary>
    public List<string> Originals { get; } = new();
    /// <summary>Valor actual de cada columna en el Excel: null, double, bool o string.</summary>
    public List<object?> Values { get; } = new();
}

/// <summary>Pestaña «como en Revit» con la informacion necesaria para importar.</summary>
public sealed class ParsedPresentationSheet
{
    public string SheetName { get; set; } = "";
    public Dictionary<string, string> Meta { get; } = new();
    /// <summary>Una por columna del cuerpo, en orden. Editable=false si no es un parametro.</summary>
    public List<EditableColumn> Columns { get; } = new();
    public List<PresentationRow> Rows { get; } = new();
    public List<string> Warnings { get; } = new();
    public char DecimalSeparator { get; set; } = ',';
    public char? GroupSeparator { get; set; } = '.';

    public string? ScheduleName => Meta.TryGetValue("ScheduleName", out var v) ? v : null;
    public string? ScheduleUniqueId => Meta.TryGetValue("ScheduleUniqueId", out var v) ? v : null;
    public string? DocumentTitle => Meta.TryGetValue("Document", out var v) ? v : null;
    public string? ExportedAt => Meta.TryGetValue("ExportedAt", out var v) ? v : null;
}

/// <summary>Excel de «TableLink» leido para importar: todas sus pestañas.</summary>
public sealed class ParsedEditableFile
{
    public List<ParsedEditableWorkbook> Sheets { get; } = new();
    /// <summary>Pestañas «como en Revit» importables (exportadas con el mapa de filas).</summary>
    public List<ParsedPresentationSheet> PresentationSheetsData { get; } = new();
    /// <summary>Pestañas «como en Revit» de una version anterior, sin mapa de filas (no importables).</summary>
    public List<string> PresentationSheets { get; } = new();
}

public enum ChangeStatus
{
    /// <summary>Se puede aplicar.</summary>
    Ready,
    /// <summary>Valor no valido o elemento inexistente.</summary>
    Error,
    /// <summary>Varias filas piden valores distintos para el mismo parametro de tipo.</summary>
    Conflict,
    Applied,
    Failed,
}

/// <summary>Un cambio de valor detectado al comparar el Excel con el modelo.</summary>
public sealed class ScheduleCellChange
{
    public string ScheduleName { get; set; } = "";
    /// <summary>Pestaña de Excel de la que sale el cambio.</summary>
    public string SheetName { get; set; } = "";
    /// <summary>Celdas de Excel (fila, columna; 1-based) que representan este cambio.</summary>
    public List<(int Row, int Column)> ExcelCells { get; } = new();
    public long ElementId { get; set; }
    /// <summary>
    /// Destinos cuando el cambio es una fila agrupada (varios elementos o
    /// tipos). Vacio = solo <see cref="ElementId"/>.
    /// </summary>
    public List<long> ElementIds { get; } = new();
    public int TargetCount => ElementIds.Count > 0 ? ElementIds.Count : 1;
    public string ElementLabel { get; set; } = "";
    public string ColumnHeading { get; set; } = "";
    public long ParameterId { get; set; }
    public bool IsType { get; set; }
    public EditableKind Kind { get; set; }
    public string OldText { get; set; } = "";
    public string NewText { get; set; } = "";
    /// <summary>Valor ya convertido para Parameter.Set (string, int o double en unidades internas).</summary>
    public object? NewValue { get; set; }
    public ChangeStatus Status { get; set; }
    public string? Message { get; set; }
    public bool IsSelected { get; set; }
}

public sealed class ScheduleImportPreview
{
    /// <summary>Tablas leidas del libro (una por pestaña editable).</summary>
    public List<string> ScheduleNames { get; } = new();
    public bool DocumentMatches { get; set; } = true;
    public string? ExportedAt { get; set; }
    public int RowsRead { get; set; }
    public List<ScheduleCellChange> Changes { get; } = new();
    public List<string> Warnings { get; } = new();
    public string? ErrorMessage { get; set; }
}

public sealed class ScheduleImportResult
{
    public bool Success { get; set; }
    public int Applied { get; set; }
    public int Failed { get; set; }
    public string? ErrorMessage { get; set; }
}
