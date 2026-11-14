using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Resultado de una operacion de escritura (ApplyEdits o FindReplace).
/// </summary>
public sealed class OneParameterEditResult
{
    public int          Modified { get; set; }
    public int          Skipped  { get; set; }
    public List<string> Warnings { get; } = new();

    public string ToSummary() =>
        $"{Modified} modificado(s), {Skipped} omitido(s), {Warnings.Count} aviso(s).";
}
