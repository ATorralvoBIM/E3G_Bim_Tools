using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Parametros de entrada para <see cref="Core.Abstractions.IOneParameterService.Search"/>.
/// </summary>
public sealed class OneParameterSearchRequest
{
    /// <summary>Nombre exacto del parametro a buscar (case-insensitive en LookupParameter).</summary>
    public string ParameterName { get; set; } = string.Empty;

    // ------------------------------------------------------------------
    // Scope
    // ------------------------------------------------------------------

    /// <summary>
    /// Conjunto de elementos sobre el que se busca.
    /// Por defecto, todo el modelo.
    /// </summary>
    public OneParameterScope Scope { get; set; } = OneParameterScope.AllModel;

    /// <summary>
    /// ID de la vista activa.  Solo se usa cuando <see cref="Scope"/> es
    /// <see cref="OneParameterScope.CurrentView"/>.
    /// </summary>
    public ElementId? ActiveViewId { get; set; }

    /// <summary>
    /// IDs de los elementos seleccionados en Revit.  Solo se usa cuando
    /// <see cref="Scope"/> es <see cref="OneParameterScope.CurrentSelection"/>.
    /// </summary>
    public IReadOnlyList<ElementId>? SelectedElementIds { get; set; }

    // ------------------------------------------------------------------
    // Filtros secundarios
    // ------------------------------------------------------------------

    /// <summary>
    /// Si no es null y tiene elementos, solo se escanean las categorias indicadas.
    /// Compatible con cualquier <see cref="Scope"/>.
    /// </summary>
    public IReadOnlyList<BuiltInCategory>? CategoryFilter { get; set; }

    /// <summary>
    /// Si true, ademas de elementos de instancia se incluyen los ElementType
    /// que tengan el parametro.  Por defecto solo instancias.
    /// </summary>
    public bool IncludeTypeElements { get; set; } = false;
}
