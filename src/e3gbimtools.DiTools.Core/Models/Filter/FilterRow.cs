namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Fila plana de la grilla de resultados.  La UI la bindea directamente al
/// DataGrid.  El servicio la rellena con los parametros principales (Categoria,
/// Familia, Tipo, Nombre) ademas de Id y Element.
/// </summary>
public sealed class FilterRow
{
    public long ElementId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
}
