namespace E3G.BimTools.DiTools.Core.Models.SheetLink;

/// <summary>
/// Resultado de una operacion de exportacion de planos a Excel (.xlsx).
/// </summary>
public sealed class SheetLinkExportResult
{
    public bool   Success        { get; set; }
    public int    SheetsExported { get; set; }

    /// <summary>Ruta completa del archivo .xlsx generado.</summary>
    public string OutputPath     { get; set; } = string.Empty;

    /// <summary>Mensaje de error si Success == false.</summary>
    public string ErrorMessage   { get; set; } = string.Empty;
}
