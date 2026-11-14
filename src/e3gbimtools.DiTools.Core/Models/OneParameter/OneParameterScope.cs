namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Define el conjunto de elementos sobre el que opera OneParameter.
/// </summary>
public enum OneParameterScope
{
    /// <summary>Todos los elementos del modelo (sin filtro de vista ni seleccion).</summary>
    AllModel,

    /// <summary>
    /// Solo los elementos visibles en la vista activa.
    /// Requiere que <c>ActiveViewId</c> este informado en la request.
    /// </summary>
    CurrentView,

    /// <summary>
    /// Solo los elementos actualmente seleccionados en Revit.
    /// Requiere que <c>SelectedElementIds</c> este informado en la request.
    /// </summary>
    CurrentSelection,
}
