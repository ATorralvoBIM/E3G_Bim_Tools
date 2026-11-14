using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Resultado de la lectura de un rango de Excel: celdas con estilos y
/// dimensiones fisicas de filas y columnas.
/// </summary>
public sealed class ExcelSheetData
{
    public string FilePath     { get; init; } = "";
    public string SheetName    { get; init; } = "";
    public string RangeAddress { get; init; } = "";

    public int RowCount    { get; init; }
    public int ColumnCount { get; init; }

    /// <summary>
    /// Celdas del rango (solo las de inicio de merge; las celdas "tapadas"
    /// por un merge no se incluyen).
    /// </summary>
    public IReadOnlyList<ExcelCellData> Cells { get; init; } =
        System.Array.Empty<ExcelCellData>();

    /// <summary>Anchos de columna en mm, clave = columna 1-based (relativa al rango).</summary>
    public IReadOnlyDictionary<int, double> ColumnWidthsMm { get; init; } =
        new Dictionary<int, double>();

    /// <summary>Alturas de fila en mm, clave = fila 1-based (relativa al rango).</summary>
    public IReadOnlyDictionary<int, double> RowHeightsMm { get; init; } =
        new Dictionary<int, double>();

    // ── Helpers de posicionamiento ───────────────────────────────────────

    /// <summary>Devuelve la coordenada X izquierda de la columna (mm, origen = 0).</summary>
    public double ColumnLeftMm(int col1Based)
    {
        double x = 0;
        for (int c = 1; c < col1Based; c++)
            x += ColumnWidthsMm.TryGetValue(c, out var w) ? w : 25.4;
        return x;
    }

    /// <summary>Devuelve la coordenada Y superior de la fila (mm, origen = 0, Y crece hacia abajo).</summary>
    public double RowTopMm(int row1Based)
    {
        double y = 0;
        for (int r = 1; r < row1Based; r++)
            y += RowHeightsMm.TryGetValue(r, out var h) ? h : 5.0;
        return y;
    }
}
