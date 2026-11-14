namespace E3G.BimTools.DiTools.Core.Models.SheetLink;

/// <summary>
/// Opciones para la operacion de exportacion de planos a CSV.
/// </summary>
public sealed class SheetLinkExportOptions
{
    /// <summary>
    /// Ruta completa del archivo CSV de destino.
    /// Si es null se devuelve el contenido como string sin escribir al disco.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>Incluir columna "Vistas colocadas".</summary>
    public bool IncludeViewsPlaced { get; set; } = true;

    /// <summary>Incluir columnas de campos de revision (fecha, numero, descripcion).</summary>
    public bool IncludeRevisionFields { get; set; } = true;

    /// <summary>Incluir columnas de firmantes (IssuedBy, IssuedTo, ApprovedBy, etc.).</summary>
    public bool IncludeSignatoryFields { get; set; } = true;

    /// <summary>
    /// Si true, exporta solo los planos cuyo ElementId esta en este conjunto.
    /// Si null o vacio exporta todos los planos del documento.
    /// </summary>
    public IReadOnlyList<int>? FilteredElementIds { get; set; }
}
