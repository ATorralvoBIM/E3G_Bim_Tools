namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Parametros de una operacion de transformacion masiva sobre un parametro.
/// </summary>
public sealed class TransformRequest
{
    /// <summary>Nombre del parametro a modificar.</summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>Tipo de transformacion a aplicar.</summary>
    public TransformOperation Operation { get; set; } = TransformOperation.SetValue;

    // ---- SetValue / Concat ------------------------------------------------

    /// <summary>
    /// Valor literal para <see cref="TransformOperation.SetValue"/>.
    /// Prefijo para <see cref="TransformOperation.Prefix"/> y
    /// <see cref="TransformOperation.Concat"/>.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Sufijo para <see cref="TransformOperation.Suffix"/> y
    /// <see cref="TransformOperation.Concat"/>.
    /// </summary>
    public string Suffix { get; set; } = string.Empty;

    // ---- RegexReplace -----------------------------------------------------

    /// <summary>Patron de expresion regular (solo para RegexReplace).</summary>
    public string Pattern     { get; set; } = string.Empty;

    /// <summary>Texto de sustitucion (puede usar grupos: $1, $2...).</summary>
    public string Replacement { get; set; } = string.Empty;

    /// <summary>Si true, el patron regex distingue mayusculas/minusculas.</summary>
    public bool   MatchCase   { get; set; } = false;
}
