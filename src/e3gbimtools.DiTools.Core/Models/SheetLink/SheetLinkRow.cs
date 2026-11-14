namespace E3G.BimTools.DiTools.Core.Models.SheetLink;

/// <summary>
/// Representa un plano (ViewSheet) del documento con todos los campos editables
/// que SheetLink puede exportar e importar.
/// </summary>
public sealed class SheetLinkRow
{
    // ----------------------------------------------------------------
    // Identidad (solo lectura — no se pueden cambiar desde SheetLink)
    // ----------------------------------------------------------------

    /// <summary>Id de Revit del ViewSheet.</summary>
    public int ElementId { get; set; }

    // ----------------------------------------------------------------
    // Campos exportables / importables
    // ----------------------------------------------------------------

    /// <summary>Numero del plano (Sheet Number).  Editable.</summary>
    public string SheetNumber { get; set; } = string.Empty;

    /// <summary>Nombre del plano (Sheet Name).  Editable.</summary>
    public string SheetName { get; set; } = string.Empty;

    /// <summary>Nombre del rotulo (cartucho) asignado al plano.</summary>
    public string TitleBlock { get; set; } = string.Empty;

    /// <summary>Revisiones visibles en el plano (lista separada por comas).</summary>
    public string Revisions { get; set; } = string.Empty;

    /// <summary>Fecha de revision actual (parametro "Revision Date").</summary>
    public string RevisionDate { get; set; } = string.Empty;

    /// <summary>Numero de revision actual (parametro "Revision Number").</summary>
    public string RevisionNumber { get; set; } = string.Empty;

    /// <summary>Descripcion de revision actual (parametro "Revision Description").</summary>
    public string RevisionDescription { get; set; } = string.Empty;

    /// <summary>Emitido por (parametro "Issued by").</summary>
    public string IssuedBy { get; set; } = string.Empty;

    /// <summary>Emitido para (parametro "Issued to").</summary>
    public string IssuedTo { get; set; } = string.Empty;

    /// <summary>Aprobado por (parametro "Approved By").</summary>
    public string ApprovedBy { get; set; } = string.Empty;

    /// <summary>Disenado por (parametro "Designed By").</summary>
    public string DesignedBy { get; set; } = string.Empty;

    /// <summary>Comprobado por (parametro "Checked By").</summary>
    public string CheckedBy { get; set; } = string.Empty;

    /// <summary>Dibujado por (parametro "Drawn By").</summary>
    public string DrawnBy { get; set; } = string.Empty;

    /// <summary>
    /// Nombres de las vistas colocadas en el plano (separadas por " | ").
    /// Solo lectura durante la importacion — la colocacion de vistas es Sprint 12.
    /// </summary>
    public string ViewsPlaced { get; set; } = string.Empty;

    // ----------------------------------------------------------------
    // Estado de importacion (no se exporta)
    // ----------------------------------------------------------------

    /// <summary>Estado del registro tras una operacion de importacion.</summary>
    public SheetLinkImportStatus ImportStatus { get; set; } = SheetLinkImportStatus.Pending;

    /// <summary>Mensaje de error o advertencia si la importacion fallo.</summary>
    public string ImportMessage { get; set; } = string.Empty;
}

/// <summary>Estado de cada fila despues de aplicar una importacion.</summary>
public enum SheetLinkImportStatus
{
    Pending,
    Updated,
    Skipped,
    Error
}
