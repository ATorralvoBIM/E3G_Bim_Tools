using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Informacion resumida de una Drafting View del documento, usada en el
/// ComboBox "Colocar vista" del tab Colocar en hoja (Sprint 9 — TableGen P2).
/// </summary>
public sealed class DraftingViewInfo
{
    public ElementId Id             { get; init; } = ElementId.InvalidElementId;
    public string    Name           { get; init; } = "";

    /// <summary>True si la vista ya esta colocada en algun Drawing Sheet.</summary>
    public bool IsPlacedOnSheet { get; init; }

    /// <summary>Texto para el combo. Indica "(ya colocada)" si aplica.</summary>
    public string DisplayName => IsPlacedOnSheet
        ? $"{Name}  (ya colocada)"
        : Name;
}
