using Autodesk.Revit.DB;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>Informacion descriptiva de un parametro Revit.</summary>
public sealed class ParameterInfo
{
    /// <summary>Nombre tal cual lo ve el usuario en Revit.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo de almacenamiento (String, Double, Integer, ElementId).</summary>
    public StorageType StorageType { get; set; }

    /// <summary>True si el parametro es de instancia (false: de tipo).</summary>
    public bool IsInstance { get; set; }

    /// <summary>True si el parametro almacena numeros (Double o Integer).</summary>
    public bool IsNumeric =>
        StorageType == StorageType.Double || StorageType == StorageType.Integer;

    public override string ToString() => Name;
}

/// <summary>Coleccion de parametros de una categoria, separados por instancia/tipo.</summary>
public sealed class CategoryParameters
{
    public System.Collections.Generic.List<ParameterInfo> Instance { get; } = new();
    public System.Collections.Generic.List<ParameterInfo> Type { get; } = new();
}
