using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

/// <summary>
/// Foto de una tabla de planificacion (ViewSchedule) tal y como se ve en Revit:
/// textos ya formateados, agrupaciones, combinaciones y colores de fondo.
/// Es independiente de la API de Revit para que el escritor Excel se pueda
/// probar sin Revit.
/// </summary>
public sealed class ScheduleSnapshot
{
    /// <summary>Nombre de la tabla (nombre de hoja y del archivo por defecto).</summary>
    public string Name { get; set; } = "";

    /// <summary>Lineas del titulo (seccion Header de Revit). Vacio → se usa <see cref="Name"/>.</summary>
    public List<string> TitleLines { get; } = new();

    /// <summary>Ancho de cada columna del cuerpo, en mm de papel.</summary>
    public List<double> ColumnWidthsMm { get; } = new();

    /// <summary>Filas de encabezado de columna (pueden ser varias si hay grupos de encabezado).</summary>
    public List<List<ScheduleCell>> HeadingRows { get; } = new();

    /// <summary>Filas del cuerpo, ya sin filas en blanco.</summary>
    public List<ScheduleRow> Rows { get; } = new();

    /// <summary>Separador decimal de las unidades del proyecto.</summary>
    public char DecimalSeparator { get; set; } = ',';

    /// <summary>Separador de miles de las unidades del proyecto (null = sin agrupar).</summary>
    public char? GroupSeparator { get; set; } = '.';

    // Metadatos (hoja _Meta oculta, base para una futura importacion)
    public string? ScheduleUniqueId { get; set; }
    public string? DocumentTitle { get; set; }

    public int ColumnCount => ColumnWidthsMm.Count;

    /// <summary>
    /// Parametro de cada columna del cuerpo (misma posicion), para poder
    /// reimportar los cambios. Vacio si no se pudo leer la definicion.
    /// </summary>
    public List<EditableColumn> ColumnFields { get; } = new();
}

public enum ScheduleRowKind
{
    /// <summary>Fila de datos (o pie/total de grupo).</summary>
    Data,
    /// <summary>Encabezado de grupo (celda combinada en todo el ancho).</summary>
    Group,
}

public sealed class ScheduleRow
{
    public ScheduleRowKind Kind { get; set; }

    /// <summary>Nivel de agrupacion, 1 = el mas externo. Solo para <see cref="ScheduleRowKind.Group"/>.</summary>
    public int Level { get; set; }

    public List<ScheduleCell> Cells { get; } = new();

    /// <summary>
    /// Elementos que forman la fila (solo filas de datos). null = no se pudo
    /// identificar; la fila no se podra importar.
    /// </summary>
    public List<long>? ElementIds { get; set; }

    /// <summary>Hay otra fila con los mismos datos visibles: no se sabe que elementos son de cual.</summary>
    public bool Ambiguous { get; set; }

    /// <summary>Texto que identifica la fila (grupos + primeras columnas) para la vista previa.</summary>
    public string Label { get; set; } = "";
}

public sealed class ScheduleCell
{
    public string Text { get; set; } = "";

    /// <summary>Fondo en hex RRGGBB, o null si es blanco / sin definir.</summary>
    public string? BackgroundHex { get; set; }

    public bool Bold { get; set; }

    /// <summary>Columnas que ocupa la celda (combinacion). 1 = normal.</summary>
    public int ColSpan { get; set; } = 1;

    /// <summary>Filas que ocupa la celda (solo encabezados agrupados). 1 = normal.</summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>Celda tapada por la combinacion de otra: no se escribe.</summary>
    public bool Covered { get; set; }
}
