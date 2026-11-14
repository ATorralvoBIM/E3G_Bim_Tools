using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Sections;

/// <summary>
/// Peticion al SectionBoxer (Sprint 5).  Inmutable: el llamante construye un
/// request nuevo cada vez.  Los campos especificos del origen son opcionales:
/// solo el que corresponde a <see cref="Kind"/> debe ir poblado.
/// </summary>
public sealed class SectionBoxRequest
{
    public SectionBoxRequest(
        SectionBoxSourceKind kind,
        double paddingMeters,
        bool includeLinks,
        SectionBoxAlignment alignment,
        bool createDedicatedView,
        string? dedicatedViewName = null,
        IReadOnlyList<ElementId>? selectedElementIds = null,
        ElementId? roomId = null,
        ElementId? levelId = null,
        string? savedFilterTemplateId = null)
    {
        Kind = kind;
        PaddingMeters = paddingMeters;
        IncludeLinks = includeLinks;
        Alignment = alignment;
        CreateDedicatedView = createDedicatedView;
        DedicatedViewName = dedicatedViewName;
        SelectedElementIds = selectedElementIds;
        RoomId = roomId;
        LevelId = levelId;
        SavedFilterTemplateId = savedFilterTemplateId;
    }

    public SectionBoxSourceKind Kind { get; }

    /// <summary>Padding en metros.  Se convierte a unidades internas (pies) en el calculo.</summary>
    public double PaddingMeters { get; }

    /// <summary>Si true, incorpora elementos de RevitLinkInstance al calculo del bounding.</summary>
    public bool IncludeLinks { get; }

    public SectionBoxAlignment Alignment { get; }

    /// <summary>Si true, crea una vista 3D dedicada con el section box aplicado.</summary>
    public bool CreateDedicatedView { get; }

    /// <summary>Nombre de la vista 3D dedicada (solo si <see cref="CreateDedicatedView"/> es true).</summary>
    public string? DedicatedViewName { get; }

    // ---- Campos especificos por tipo de origen ----

    public IReadOnlyList<ElementId>? SelectedElementIds { get; }
    public ElementId? RoomId { get; }
    public ElementId? LevelId { get; }
    public string? SavedFilterTemplateId { get; }

    /// <summary>
    /// Validacion basica sin tocar el documento.  No comprueba la existencia
    /// de los Ids; eso es responsabilidad del resolver del origen.
    /// </summary>
    public bool IsValid(out string? error)
    {
        if (PaddingMeters < 0) { error = "El padding no puede ser negativo."; return false; }
        switch (Kind)
        {
            case SectionBoxSourceKind.Selection when SelectedElementIds is null || SelectedElementIds.Count == 0:
                error = "No hay elementos seleccionados."; return false;
            case SectionBoxSourceKind.Room when RoomId is null:
                error = "Falta la habitacion."; return false;
            case SectionBoxSourceKind.Level when LevelId is null:
                error = "Falta el nivel."; return false;
            case SectionBoxSourceKind.SavedFilter when string.IsNullOrWhiteSpace(SavedFilterTemplateId):
                error = "Falta el filtro guardado."; return false;
        }
        if (CreateDedicatedView && string.IsNullOrWhiteSpace(DedicatedViewName))
        {
            error = "Pon un nombre para la vista 3D dedicada."; return false;
        }
        error = null; return true;
    }
}
