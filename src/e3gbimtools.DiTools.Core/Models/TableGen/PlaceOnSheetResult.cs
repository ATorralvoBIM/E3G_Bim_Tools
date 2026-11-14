using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Resultado de colocar una Drafting View sobre un Drawing Sheet
/// (Sprint 9 — TableGen P2).
/// </summary>
public sealed class PlaceOnSheetResult
{
    public bool       Success    { get; init; }
    public ElementId? ViewportId { get; init; }
    public string?    ErrorMessage { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } =
        System.Array.Empty<string>();
}
