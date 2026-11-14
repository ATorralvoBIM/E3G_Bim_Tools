namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Una sola condicion del editor de filtros: parametro + operador + valor.
///
/// Notas:
///  - <see cref="ParameterName"/> coincide con el nombre visible del parametro
///    en Revit (lo que ve el usuario en la paleta de propiedades).  Acepta tanto
///    parametros de instancia como de tipo; el servicio prueba primero instancia
///    y, si no, cae al tipo.
///  - <see cref="Value"/> es siempre texto.  La conversion a numerico (si el
///    parametro es Double/Integer) ocurre en <c>FilterService</c>.
///  - <see cref="UseTypeParameter"/> fuerza la lectura desde el tipo aunque el
///    elemento de instancia tenga un parametro homonimo.  Util cuando el usuario
///    elige explicitamente desde la lista "(Tipo) NombreParam".
/// </summary>
public sealed class FilterCondition
{
    public string ParameterName { get; set; } = string.Empty;
    public FilterOperator Operator { get; set; } = FilterOperator.Equal;
    public string Value { get; set; } = string.Empty;
    public bool UseTypeParameter { get; set; } = false;

    /// <summary>Devuelve true si la condicion esta minimamente formada para evaluarse.</summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ParameterName)) return false;
        // IsEmpty/IsNotEmpty no necesitan valor.
        if (Operator is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty) return true;
        return Value is not null;
    }
}
