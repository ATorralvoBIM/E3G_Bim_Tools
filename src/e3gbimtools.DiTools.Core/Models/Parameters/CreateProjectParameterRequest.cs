using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Parameters;

/// <summary>
/// Datos necesarios para crear un parametro de proyecto o compartido nuevo y
/// vincularlo a una o varias categorias.
/// </summary>
public sealed class CreateProjectParameterRequest
{
    public string Name { get; set; } = string.Empty;
    public ParameterDataTypeOption DataType { get; set; } = ParameterDataTypeOption.Text;
    public ParameterScope Scope { get; set; } = ParameterScope.Instance;

    /// <summary>Si es true, se crea un parametro COMPARTIDO; si false, parametro de PROYECTO.</summary>
    public bool CreateAsShared { get; set; } = true;

    /// <summary>Grupo en el .txt de parametros compartidos (solo si <see cref="CreateAsShared"/>).</summary>
    public string SharedGroupName { get; set; } = "e3gbimtools";

    /// <summary>Nombre del grupo en el que aparecera dentro de las propiedades del elemento.</summary>
    public string PropertiesGroupName { get; set; } = "Datos";

    public IList<BuiltInCategory> Categories { get; set; } = new List<BuiltInCategory>();
}
