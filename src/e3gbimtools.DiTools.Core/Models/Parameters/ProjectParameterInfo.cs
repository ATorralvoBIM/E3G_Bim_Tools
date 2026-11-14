using System;
using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Parameters;

/// <summary>
/// Vista de un parametro de proyecto/compartido tal y como esta registrado en
/// el BindingMap del documento.  Esta es la fila principal del DataGrid de la
/// pestaña "Lista de parametros" de ParaManager.
/// </summary>
public sealed class ProjectParameterInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Origen (Project / Shared).  BuiltIn/Family no aparecen aqui.</summary>
    public ParameterSource Source { get; set; } = ParameterSource.Project;

    public ParameterScope Scope { get; set; } = ParameterScope.Instance;

    /// <summary>Nombre del grupo en el que aparece en el "Properties" panel de Revit.</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Tipo de dato (texto, longitud, area, etc.) tal cual lo expone Revit.</summary>
    public string DataTypeName { get; set; } = string.Empty;

    /// <summary>Nombres de las categorias a las que esta vinculado.</summary>
    public IList<string> BoundCategoryNames { get; set; } = new List<string>();

    /// <summary>Ids (long) de las BuiltInCategory enlazadas — para recategorizar.</summary>
    public IList<long> BoundCategoryIds { get; set; } = new List<long>();

    /// <summary>GUID solo para parametros compartidos.</summary>
    public Guid? SharedGuid { get; set; }

    /// <summary>InternalDefinition.Id del parametro — clave para localizarlo al borrar/reasignar.</summary>
    public long DefinitionElementId { get; set; }

    public override string ToString() => Name;
}
