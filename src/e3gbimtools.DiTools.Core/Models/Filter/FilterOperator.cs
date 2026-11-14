namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Operadores soportados por una condicion del OneFilter.
/// La interpretacion concreta (numerico vs texto) la decide
/// <c>FilterService</c> segun el tipo de StorageType del parametro.
/// </summary>
public enum FilterOperator
{
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    IsEmpty,
    IsNotEmpty,
}
