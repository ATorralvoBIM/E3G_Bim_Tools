using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Parameters;

/// <summary>
/// Resultado de una operacion de ParaManager (crear/borrar/recategorizar).
/// Sigue el mismo patron que <c>LevelCopyResult</c> y <c>BatchEditResult</c>.
/// </summary>
public sealed class ParaManagerActionResult
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public IList<string> Warnings { get; set; } = new List<string>();

    public string ToSummary() => string.IsNullOrEmpty(Summary)
        ? (Success ? "Operacion completada." : "Operacion fallida.")
        : Summary;
}
