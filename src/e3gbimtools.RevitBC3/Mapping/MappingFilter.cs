namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Un filtro individual sobre un parametro de Revit.
/// Una regla puede tener N filtros — todos deben cumplirse (AND logico).
/// </summary>
public sealed class MappingFilter
{
    /// <summary>Nombre del parametro Revit (instancia o tipo, se busca primero en instancia).</summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>
    /// Texto a mostrar en la UI.  Habitualmente coincide con
    /// <see cref="ParameterName"/>, pero puede llevar mas info para el usuario.
    /// </summary>
    public string ParameterDisplayName { get; set; } = string.Empty;

    /// <summary>Operador de comparacion.</summary>
    public MappingOperator Operator { get; set; } = MappingOperator.Equals;

    /// <summary>Valor a comparar (texto; el motor lo convierte segun el operador).</summary>
    public string Value { get; set; } = string.Empty;

    public override string ToString()
    {
        var op = Operator switch
        {
            MappingOperator.Equals         => "=",
            MappingOperator.NotEquals      => "!=",
            MappingOperator.GreaterThan    => ">",
            MappingOperator.GreaterOrEqual => ">=",
            MappingOperator.LessThan       => "<",
            MappingOperator.LessOrEqual    => "<=",
            MappingOperator.Contains       => "contiene",
            MappingOperator.StartsWith     => "empieza por",
            MappingOperator.EndsWith       => "termina por",
            MappingOperator.Regex          => "regex",
            _                              => "?",
        };
        var name = string.IsNullOrEmpty(ParameterDisplayName) ? ParameterName : ParameterDisplayName;
        return $"{name} {op} {Value}";
    }
}
