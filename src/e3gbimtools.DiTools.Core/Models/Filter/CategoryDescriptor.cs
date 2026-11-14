using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Descriptor ligero de una categoria de Revit para la UI: nombre localizado +
/// BuiltInCategory + flag de "es categoria visible" para descartar las internas
/// que no aportan valor en un filtro funcional.
/// </summary>
public sealed class CategoryDescriptor
{
    public string Name { get; set; } = string.Empty;
    public BuiltInCategory BuiltInCategory { get; set; } = BuiltInCategory.INVALID;
    public long CategoryId { get; set; }
    public bool IsSelected { get; set; } = false;

    public override string ToString() => Name;
}
