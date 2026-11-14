using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Informacion resumida de un Drawing Sheet (ViewSheet), usada en los
/// ComboBox "Colocar en hoja" de TableGen P2 (Sprint 9).
/// </summary>
public sealed class SheetPickerInfo
{
    public ElementId Id          { get; init; } = ElementId.InvalidElementId;
    public string    SheetNumber { get; init; } = "";
    public string    SheetName   { get; init; } = "";

    /// <summary>Texto para el combo: "A101 — Planta Baja".</summary>
    public string DisplayName => $"{SheetNumber} — {SheetName}";
}
