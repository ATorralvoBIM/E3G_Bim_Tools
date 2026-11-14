using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Resultado de la generacion de una Drafting View por TableGen (Sprint 8).
/// </summary>
public sealed class TableGenResult
{
    public bool     Success        { get; init; }
    public ElementId? DraftingViewId { get; init; }
    public string?  ViewName       { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } =
        System.Array.Empty<string>();

    /// <summary>Tiempo total de generacion en milisegundos.</summary>
    public long   ElapsedMs    { get; init; }
    public string? ErrorMessage { get; init; }
}
