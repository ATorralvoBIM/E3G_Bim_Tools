namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Fila de resultado de una busqueda OneParameter.  Representa un elemento
/// del modelo que posee el parametro buscado.
///
/// <see cref="EditedValue"/> es el valor que el usuario quiere escribir.
/// <see cref="IsModified"/> se pone a true cuando EditedValue difiere de
/// CurrentValue; <see cref="IsReadOnly"/> indica si la escritura esta
/// bloqueada por Revit.
/// </summary>
public sealed class OneParameterRow
{
    public long   ElementId    { get; set; }
    public string Category     { get; set; } = string.Empty;
    public string Family       { get; set; } = string.Empty;
    public string TypeName     { get; set; } = string.Empty;
    public string ElementName  { get; set; } = string.Empty;
    public string Level        { get; set; } = string.Empty;

    /// <summary>Valor actual leido de Revit (se actualiza tras ApplyEdits).</summary>
    public string CurrentValue { get; set; } = string.Empty;

    /// <summary>Valor que el usuario ha editado en el DataGrid.</summary>
    public string EditedValue  { get; set; } = string.Empty;

    public bool   IsModified   { get; set; }
    public bool   IsReadOnly   { get; set; }

    /// <summary>Nombre del parametro (necesario para el servicio de escritura).</summary>
    public string ParameterName { get; set; } = string.Empty;
}
