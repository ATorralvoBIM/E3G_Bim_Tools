namespace E3G.BimTools.DiTools.Core.Models.Sheets;

/// <summary>
/// Especifica dónde y a qué escala colocar una vista dentro de una sheet
/// (Sprint 6 — SheetGen).  Las coordenadas son en milímetros respecto al
/// origen del title block; se convierten a pies (unidades internas Revit)
/// al llamar a <c>Viewport.Create</c>.
/// </summary>
public sealed class ViewPlacementSpec
{
    /// <summary>Nombre exacto de la vista a colocar.</summary>
    public string ViewName { get; set; } = string.Empty;

    /// <summary>Desplazamiento horizontal desde el origen del title block (mm).</summary>
    public double XOffsetMm { get; set; }

    /// <summary>Desplazamiento vertical desde el origen del title block (mm).</summary>
    public double YOffsetMm { get; set; }

    /// <summary>
    /// Escala a aplicar a la vista al colocarla (ej. 50 = 1:50).
    /// Null = mantener la escala que ya tiene la vista.
    /// </summary>
    public int? ScaleOverride { get; set; }
}
