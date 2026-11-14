using Autodesk.Revit.DB;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Informacion descriptiva de una categoria Revit presente en el modelo activo.
/// </summary>
public sealed class RevitCategoryInfo
{
    /// <summary>Identificador estable independiente del idioma.</summary>
    public BuiltInCategory BuiltInCategory { get; set; } = BuiltInCategory.INVALID;

    /// <summary>Nombre localizado tal cual lo ve el usuario en Revit (ej. "Conductos").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Numero de elementos de esta categoria presentes en el modelo (excluye tipos).</summary>
    public int ElementCount { get; set; }

    public override string ToString() =>
        ElementCount > 0 ? $"{DisplayName} ({ElementCount})" : DisplayName;
}
