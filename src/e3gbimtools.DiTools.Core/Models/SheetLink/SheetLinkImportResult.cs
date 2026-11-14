namespace E3G.BimTools.DiTools.Core.Models.SheetLink;

/// <summary>
/// Resultado de una operacion de importacion / aplicacion de cambios al modelo.
/// </summary>
public sealed class SheetLinkImportResult
{
    public bool   Success        { get; set; }
    public int    Updated        { get; set; }
    public int    Skipped        { get; set; }
    public int    Errors         { get; set; }
    public string Summary        { get; set; } = string.Empty;

    /// <summary>Filas procesadas con su estado individual.</summary>
    public IReadOnlyList<SheetLinkRow> Rows { get; set; } = Array.Empty<SheetLinkRow>();
}
