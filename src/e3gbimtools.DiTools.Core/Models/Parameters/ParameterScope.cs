namespace E3G.BimTools.DiTools.Core.Models.Parameters;

/// <summary>
/// Como se vincula un parametro a una categoria: en cada instancia (cambia
/// elemento a elemento) o en el tipo (cambia para todas las instancias del
/// tipo).
/// </summary>
public enum ParameterScope
{
    Instance,
    Type,
}
