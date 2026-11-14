namespace E3G.BimTools.DiTools.Core.Models.Filter;

/// <summary>
/// Operacion de edicion en lote sobre los resultados del OneFilter.  Aplica un
/// nuevo valor a un parametro en todos los elementos seleccionados.
///
/// Si <see cref="ApplyToType"/> es true, el cambio se intenta sobre el tipo
/// (afecta a TODAS las instancias del tipo, no solo a las filtradas).
/// </summary>
public sealed class BatchEditOperation
{
    public string ParameterName { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public bool ApplyToType { get; set; } = false;
}

/// <summary>Resultado de una operacion BatchEdit.</summary>
public sealed class BatchEditResult
{
    public int Modified { get; set; }
    public int Skipped { get; set; }
    public System.Collections.Generic.List<string> Warnings { get; } = new();

    public string ToSummary() =>
        $"{Modified} modificado(s), {Skipped} omitido(s), {Warnings.Count} aviso(s).";
}
