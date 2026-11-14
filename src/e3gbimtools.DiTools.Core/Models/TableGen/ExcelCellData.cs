namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Datos de una celda Excel leida con ClosedXML (Sprint 8 — TableGen).
/// Incluye valor, estilos de fuente, colores y bordes.
/// Si la celda forma parte de un merge, <see cref="MergedArea"/> describe
/// el bloque completo; solo se genera la celda de inicio del merge.
/// </summary>
public sealed class ExcelCellData
{
    // ── Posicion (1-based, relativa al rango leido) ─────────────────────
    public int Row    { get; init; }
    public int Column { get; init; }

    // ── Contenido ───────────────────────────────────────────────────────
    public string? Value { get; init; }

    // ── Merge ───────────────────────────────────────────────────────────
    /// <summary>Null si la celda no pertenece a ningun merge.</summary>
    public MergedArea? MergedArea { get; init; }

    // ── Fuente ──────────────────────────────────────────────────────────
    public string FontName   { get; init; } = "Arial";
    public double FontSizePt { get; init; } = 10.0;
    public bool   Bold       { get; init; }
    public bool   Italic     { get; init; }

    // ── Colores (ARGB, 0xFF000000 = negro opaco) ─────────────────────────
    public uint FontColor       { get; init; } = 0xFF000000;
    public uint BackgroundColor { get; init; } = 0xFFFFFFFF;
    public bool HasBackground   { get; init; }

    // ── Bordes ──────────────────────────────────────────────────────────
    public CellBorderStyle TopBorder    { get; init; } = CellBorderStyle.None;
    public CellBorderStyle BottomBorder { get; init; } = CellBorderStyle.None;
    public CellBorderStyle LeftBorder   { get; init; } = CellBorderStyle.None;
    public CellBorderStyle RightBorder  { get; init; } = CellBorderStyle.None;

    // ── Dimensiones fisicas (mm, de la celda o del bloque merge) ─────────
    public double WidthMm  { get; init; }
    public double HeightMm { get; init; }
}

/// <summary>
/// Describe el bloque de celdas combinadas al que pertenece una celda
/// (coordenadas 1-based relativas al rango leido).
/// </summary>
public sealed record MergedArea(
    int FirstRow, int FirstColumn,
    int LastRow,  int LastColumn)
{
    public bool IsStart(int row, int col) =>
        row == FirstRow && col == FirstColumn;

    public int RowSpan    => LastRow  - FirstRow    + 1;
    public int ColumnSpan => LastColumn - FirstColumn + 1;
}

public enum CellBorderStyle { None, Thin, Medium, Thick, Double }
