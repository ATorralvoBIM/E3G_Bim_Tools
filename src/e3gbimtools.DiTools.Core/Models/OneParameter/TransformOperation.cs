namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Tipo de transformacion que se aplica al valor actual de un parametro de texto.
/// </summary>
public enum TransformOperation
{
    /// <summary>Sustituye el valor completo por el texto indicado.</summary>
    SetValue,

    /// <summary>Antepone un prefijo: <c>prefix + valor_actual</c>.</summary>
    Prefix,

    /// <summary>Pospone un sufijo: <c>valor_actual + suffix</c>.</summary>
    Suffix,

    /// <summary>
    /// Combina prefijo, valor actual y sufijo:
    /// <c>prefix + valor_actual + suffix</c>.
    /// </summary>
    Concat,

    /// <summary>
    /// Sustitucion con regex: aplica <see cref="TransformRequest.Pattern"/> y
    /// reemplaza con <see cref="TransformRequest.Replacement"/>.
    /// Solo funciona con parametros de tipo String.
    /// </summary>
    RegexReplace,
}
