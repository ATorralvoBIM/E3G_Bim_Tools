using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Resultado de la generacion en lote (Sprint 9 — TableGen P2).
/// Contiene un <see cref="TableGenResult"/> por cada hoja procesada.
/// </summary>
public sealed class BatchTableGenResult
{
    public IReadOnlyList<TableGenResult> Results { get; init; } =
        System.Array.Empty<TableGenResult>();

    /// <summary>Numero de vistas generadas correctamente.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Numero de hojas que fallaron.</summary>
    public int FailCount { get; init; }

    /// <summary>Tiempo total (milisegundos) para todo el lote.</summary>
    public long TotalElapsedMs { get; init; }
}
