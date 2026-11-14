namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Operadores de comparacion soportados en los filtros de una regla de mapeo.
/// </summary>
public enum MappingOperator
{
    /// <summary>Igualdad exacta de strings (case-insensitive).</summary>
    Equals,

    /// <summary>Desigualdad.</summary>
    NotEquals,

    /// <summary>Numerico: mayor que.</summary>
    GreaterThan,

    /// <summary>Numerico: mayor o igual que.</summary>
    GreaterOrEqual,

    /// <summary>Numerico: menor que.</summary>
    LessThan,

    /// <summary>Numerico: menor o igual que.</summary>
    LessOrEqual,

    /// <summary>String contiene (case-insensitive).</summary>
    Contains,

    /// <summary>String empieza por (case-insensitive).</summary>
    StartsWith,

    /// <summary>String termina por (case-insensitive).</summary>
    EndsWith,

    /// <summary>Expresion regular (.NET).</summary>
    Regex,
}
