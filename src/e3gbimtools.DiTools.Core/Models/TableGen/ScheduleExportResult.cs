using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Resultado de la conversion Schedule → Drafting View
/// (Sprint 9 — TableGen P2).
/// </summary>
public sealed class ScheduleExportResult
{
    public bool       Success        { get; init; }
    public ElementId? DraftingViewId { get; init; }
    public string?    ViewName       { get; init; }

    /// <summary>Numero de filas procesadas (cabecera + cuerpo).</summary>
    public int RowCount { get; init; }

    /// <summary>Numero de columnas del schedule.</summary>
    public int ColumnCount { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } =
        System.Array.Empty<string>();

    public long    ElapsedMs    { get; init; }
    public string? ErrorMessage { get; init; }
}
