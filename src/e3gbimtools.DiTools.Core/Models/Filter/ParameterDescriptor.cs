namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Descriptor ligero de un parametro disponible en las categorias seleccionadas.
/// Lo usa el combo "Parametro" del editor de condiciones.
///
/// <see cref="DisplayName"/> es lo que ve Angel en el desplegable (con prefijo
/// "(Tipo)" si <see cref="IsTypeParameter"/>).
/// </summary>
public sealed class ParameterDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsTypeParameter { get; set; } = false;

    /// <summary>StorageType ("String", "Double", "Integer", "ElementId") como texto, para hint en UI.</summary>
    public string StorageTypeName { get; set; } = "String";

    public override string ToString() => DisplayName;
}
