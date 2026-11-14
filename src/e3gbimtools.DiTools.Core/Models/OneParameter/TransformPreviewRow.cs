namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Fila de preview Before/After para una operacion de transformacion.
/// </summary>
public sealed class TransformPreviewRow
{
    public long   ElementId   { get; set; }
    public string Category    { get; set; } = string.Empty;
    public string ElementName { get; set; } = string.Empty;
    public string Level       { get; set; } = string.Empty;

    /// <summary>Valor actual leido de Revit.</summary>
    public string Before      { get; set; } = string.Empty;

    /// <summary>Valor calculado tras aplicar la transformacion.</summary>
    public string After       { get; set; } = string.Empty;

    /// <summary>True si After != Before (se escribira en Revit).</summary>
    public bool   WillChange  => !string.Equals(Before, After, System.StringComparison.Ordinal);

    /// <summary>Mensaje de error si la transformacion no pudo calcularse.</summary>
    public string? Error      { get; set; }
}
