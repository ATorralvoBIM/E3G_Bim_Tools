using System;
using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Sheets;

/// <summary>
/// Define un layout de colocación de vistas dentro de una Drawing Sheet
/// (Sprint 6 — SheetGen).  Se persiste como JSON en
/// %APPDATA%\e3g\DiTools\SheetLayouts\ para reutilizarse entre sesiones.
/// </summary>
public sealed class SheetLayoutDefinition
{
    /// <summary>Identificador único del layout.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nombre visible en la UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Descripción opcional.</summary>
    public string? Description { get; set; }

    /// <summary>Vistas y sus posiciones.</summary>
    public List<ViewPlacementSpec> Placements { get; set; } = new();
}
