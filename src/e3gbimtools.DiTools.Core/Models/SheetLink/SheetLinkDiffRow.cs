namespace E3G.BimTools.DiTools.Core.Models.SheetLink;

/// <summary>
/// Representa un plano con sus valores actuales en el modelo Revit (Before)
/// y los valores del archivo Excel editado (After), para la preview de
/// importacion con diff (Sprint 12 — SheetLink P2).
///
/// <list type="bullet">
///   <item><description><c>Status = Unchanged</c>: todos los campos coinciden, no se aplicara nada.</description></item>
///   <item><description><c>Status = Modified</c>: al menos un campo difiere; se puede aplicar si <c>IsSelected = true</c>.</description></item>
///   <item><description><c>Status = Missing</c>: el <c>ElementId</c> no existe en el modelo actual; no se aplicara.</description></item>
/// </list>
/// </summary>
public sealed class SheetLinkDiffRow
{
    // ----------------------------------------------------------------
    // Identidad y estado del diff
    // ----------------------------------------------------------------

    /// <summary>Id de Revit del ViewSheet.</summary>
    public int ElementId { get; set; }

    /// <summary>Estado del diff: Unchanged, Modified o Missing.</summary>
    public DiffStatus Status { get; set; } = DiffStatus.Unchanged;

    /// <summary>
    /// Si <c>true</c>, la fila sera aplicada al modelo al confirmar.
    /// Solo tiene efecto cuando <c>Status == Modified</c>.
    /// Las filas <c>Modified</c> se preseleccionan automaticamente (IsSelected=true)
    /// y las demas (Unchanged/Missing) no (IsSelected=false).
    /// </summary>
    public bool IsSelected { get; set; }

    // ----------------------------------------------------------------
    // Before = valor actual en el modelo Revit
    // After  = valor del .xlsx editado por el usuario
    // ----------------------------------------------------------------

    public string SheetNumber_Before { get; set; } = string.Empty;
    public string SheetNumber_After  { get; set; } = string.Empty;

    public string SheetName_Before { get; set; } = string.Empty;
    public string SheetName_After  { get; set; } = string.Empty;

    public string RevisionDate_Before { get; set; } = string.Empty;
    public string RevisionDate_After  { get; set; } = string.Empty;

    public string RevisionNumber_Before { get; set; } = string.Empty;
    public string RevisionNumber_After  { get; set; } = string.Empty;

    public string RevisionDescription_Before { get; set; } = string.Empty;
    public string RevisionDescription_After  { get; set; } = string.Empty;

    public string IssuedBy_Before { get; set; } = string.Empty;
    public string IssuedBy_After  { get; set; } = string.Empty;

    public string IssuedTo_Before { get; set; } = string.Empty;
    public string IssuedTo_After  { get; set; } = string.Empty;

    public string ApprovedBy_Before { get; set; } = string.Empty;
    public string ApprovedBy_After  { get; set; } = string.Empty;

    public string DesignedBy_Before { get; set; } = string.Empty;
    public string DesignedBy_After  { get; set; } = string.Empty;

    public string CheckedBy_Before { get; set; } = string.Empty;
    public string CheckedBy_After  { get; set; } = string.Empty;

    public string DrawnBy_Before { get; set; } = string.Empty;
    public string DrawnBy_After  { get; set; } = string.Empty;

    // ----------------------------------------------------------------
    // Computed — diferencias
    // ----------------------------------------------------------------

    /// <summary>True si al menos un campo tiene valor diferente entre Before y After.</summary>
    public bool HasChanges =>
        SheetNumber_Before         != SheetNumber_After         ||
        SheetName_Before           != SheetName_After           ||
        RevisionDate_Before        != RevisionDate_After        ||
        RevisionNumber_Before      != RevisionNumber_After      ||
        RevisionDescription_Before != RevisionDescription_After ||
        IssuedBy_Before            != IssuedBy_After            ||
        IssuedTo_Before            != IssuedTo_After            ||
        ApprovedBy_Before          != ApprovedBy_After          ||
        DesignedBy_Before          != DesignedBy_After          ||
        CheckedBy_Before           != CheckedBy_After           ||
        DrawnBy_Before             != DrawnBy_After;

    /// <summary>Numero de campos que difieren entre Before y After.</summary>
    public int ChangedFieldCount => new bool[]
    {
        SheetNumber_Before         != SheetNumber_After,
        SheetName_Before           != SheetName_After,
        RevisionDate_Before        != RevisionDate_After,
        RevisionNumber_Before      != RevisionNumber_After,
        RevisionDescription_Before != RevisionDescription_After,
        IssuedBy_Before            != IssuedBy_After,
        IssuedTo_Before            != IssuedTo_After,
        ApprovedBy_Before          != ApprovedBy_After,
        DesignedBy_Before          != DesignedBy_After,
        CheckedBy_Before           != CheckedBy_After,
        DrawnBy_Before             != DrawnBy_After,
    }.Count(b => b);

    // ----------------------------------------------------------------
    // Estado de aplicacion (se rellena despues de llamar a ApplyDiff)
    // ----------------------------------------------------------------

    /// <summary>Estado del registro tras la operacion de importacion.</summary>
    public SheetLinkImportStatus ApplyStatus  { get; set; } = SheetLinkImportStatus.Pending;

    /// <summary>Mensaje de error o advertencia si la importacion fallo.</summary>
    public string                ApplyMessage { get; set; } = string.Empty;
}

/// <summary>Estado del diff de una fila respecto al modelo actual.</summary>
public enum DiffStatus
{
    /// <summary>Todos los campos del Excel coinciden con el modelo. No se aplicara nada.</summary>
    Unchanged,

    /// <summary>Al menos un campo difiere. Se puede aplicar si <c>IsSelected = true</c>.</summary>
    Modified,

    /// <summary>El ElementId del Excel no existe en el modelo. No se puede aplicar.</summary>
    Missing,
}
