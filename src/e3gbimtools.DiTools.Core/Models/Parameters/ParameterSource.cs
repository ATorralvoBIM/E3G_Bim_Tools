namespace E3G.BimTools.DiTools.Core.Models.Parameters;

/// <summary>
/// Origen de un parametro listado en ParaManager.  Determina si se puede
/// editar/eliminar (Project y Shared se pueden gestionar, BuiltIn y Family no).
/// </summary>
public enum ParameterSource
{
    /// <summary>Parametro de proyecto (creado en el modelo, sin GUID).</summary>
    Project,

    /// <summary>Parametro compartido (definido en un .txt de shared parameters).</summary>
    Shared,

    /// <summary>Parametro nativo de Revit (built-in).  Solo lectura.</summary>
    BuiltIn,

    /// <summary>Parametro propio de una familia.  Solo lectura desde aqui.</summary>
    Family,
}
