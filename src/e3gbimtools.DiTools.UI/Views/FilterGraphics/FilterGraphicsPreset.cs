using System;
using System.Collections.Generic;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Fichero JSON con el grafismo de filtros de una vista (lo que muestra la
/// pestaña Filtros de V/G: activo, visible, proyección/superficie, corte,
/// transparencia y medio tono). Independiente de vistas y plantillas: se
/// reaplica a cualquier vista de cualquier proyecto casando por NOMBRE de filtro.
///
/// Los patrones de línea y relleno se guardan por nombre (los ElementId cambian
/// entre proyectos). Un valor <c>null</c> significa "sin modificación".
/// </summary>
public sealed class FilterGraphicsPreset
{
    /// <summary>1 = solo grafismo; 2 = grafismo + definición (categorías y reglas).</summary>
    public int      Version    { get; set; } = 2;
    public string?  SourceDocument { get; set; }
    public string?  SourceView { get; set; }
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<FilterGraphicsEntry> Filters { get; set; } = new();
}

public sealed class FilterGraphicsEntry
{
    public string Name     { get; set; } = string.Empty;
    public bool   Enabled  { get; set; } = true;
    public bool   Visible  { get; set; } = true;
    public bool   Halftone { get; set; }

    /// <summary>Transparencia de superficie 0-100.</summary>
    public int    Transparency { get; set; }

    public LineGraphics?    ProjectionLine      { get; set; }
    public PatternGraphics? SurfaceForeground   { get; set; }
    public PatternGraphics? SurfaceBackground   { get; set; }
    public LineGraphics?    CutLine             { get; set; }
    public PatternGraphics? CutForeground       { get; set; }
    public PatternGraphics? CutBackground       { get; set; }

    /// <summary>
    /// Categorías y reglas del filtro (solo filtros de reglas; <c>null</c> en
    /// filtros de selección). Permite crear el filtro si no existe en el destino
    /// y detectar homónimos con reglas distintas.
    /// </summary>
    public FilterDefinitionDto? Definition { get; set; }

    /// <summary>¿Tiene alguna modificación respecto a "por defecto"?</summary>
    public bool HasAnyOverride =>
        !Visible || Halftone || Transparency > 0 ||
        ProjectionLine is not null || SurfaceForeground is not null || SurfaceBackground is not null ||
        CutLine is not null || CutForeground is not null || CutBackground is not null;

    /// <summary>Copia sin definición: sirve para comparar solo el grafismo.</summary>
    public FilterGraphicsEntry GraphicsOnly() => new()
    {
        Name = Name, Enabled = Enabled, Visible = Visible, Halftone = Halftone, Transparency = Transparency,
        ProjectionLine = ProjectionLine, SurfaceForeground = SurfaceForeground, SurfaceBackground = SurfaceBackground,
        CutLine = CutLine, CutForeground = CutForeground, CutBackground = CutBackground,
    };
}

// ---------------------------------------------------------------------- //
//  Definición del filtro
// ---------------------------------------------------------------------- //

public sealed class FilterDefinitionDto
{
    /// <summary>Nombres de BuiltInCategory (p. ej. "OST_PipeCurves").</summary>
    public List<string> Categories { get; set; } = new();
    /// <summary>Árbol de reglas; <c>null</c> = filtro solo por categorías.</summary>
    public FilterNodeDto? Filter { get; set; }
}

public sealed class FilterNodeDto
{
    /// <summary>"And", "Or" o "Param".</summary>
    public string Kind { get; set; } = "Param";
    public bool   Inverted { get; set; }
    public List<FilterNodeDto>? Children { get; set; }
    public List<FilterRuleDto>?  Rules    { get; set; }
}

public sealed class FilterRuleDto
{
    public ParamRefDto Parameter { get; set; } = new();
    /// <summary>"String", "Double", "Integer", "ElementId", "HasValue", "HasNoValue".</summary>
    public string  Type      { get; set; } = "String";
    /// <summary>"Equals", "BeginsWith", "EndsWith", "Contains", "Greater", "GreaterOrEqual", "Less", "LessOrEqual".</summary>
    public string? Evaluator { get; set; }
    /// <summary>Regla negada (No es igual a, No contiene…).</summary>
    public bool    Inverse   { get; set; }

    public string? String  { get; set; }
    public double? Double  { get; set; }
    public double? Epsilon { get; set; }
    public int?    Integer { get; set; }
    public ElementRefDto? Element { get; set; }
}

/// <summary>Parámetro: BuiltInParameter por nombre, compartido por GUID o de proyecto por nombre.</summary>
public sealed class ParamRefDto
{
    public string? BuiltIn { get; set; }
    public Guid?   Guid    { get; set; }
    public string? Name    { get; set; }

    public override string ToString() => Name ?? BuiltIn ?? Guid?.ToString() ?? "?";
}

/// <summary>Valor ElementId de una regla (tipo de sistema, nivel…): por clase y nombre.</summary>
public sealed class ElementRefDto
{
    /// <summary>Id negativo/invalid (valores built-in o "ninguno"): se guarda tal cual.</summary>
    public long?   RawId { get; set; }
    public string? Class { get; set; }
    public string? Name  { get; set; }
}

/// <summary>Modificación de línea: color "#RRGGBB", patrón por nombre, grosor 1-16.</summary>
public sealed class LineGraphics
{
    public string? Color   { get; set; }
    /// <summary>Nombre del patrón de línea; "Solid" = continua.</summary>
    public string? Pattern { get; set; }
    public int?    Weight  { get; set; }

    public bool IsEmpty => Color is null && Pattern is null && Weight is null;
}

/// <summary>Modificación de patrón de relleno (primer plano o fondo).</summary>
public sealed class PatternGraphics
{
    public bool    Visible { get; set; } = true;
    public string? Color   { get; set; }
    /// <summary>Nombre del FillPatternElement.</summary>
    public string? Pattern { get; set; }
    /// <summary>"Drafting" o "Model" (desambigua patrones homónimos).</summary>
    public string? Target  { get; set; }

    public bool IsEmpty => Visible && Color is null && Pattern is null;
}
