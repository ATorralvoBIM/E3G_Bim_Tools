using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Abstraccion sobre <see cref="UIApplication"/> que permite a los servicios
/// no depender directamente del singleton de Revit.  La implementacion concreta
/// vive en e3gbimtools.DiTools (proyecto host) porque ella si conoce
/// <c>ExternalCommandData</c>.
/// </summary>
public interface IRevitContext
{
    /// <summary>
    /// UIApplication actual.  Lanza si Revit aun no ha terminado de iniciar.
    /// Disponible solo despues del evento <c>ApplicationInitialized</c>.
    /// </summary>
    UIApplication UiApp { get; }

    /// <summary>
    /// ControlledApplication — subset siempre disponible (incluso durante
    /// IExternalApplication.OnStartup, antes de que Revit termine de cargar).
    /// </summary>
    ControlledApplication ControlledApp { get; }

    /// <summary>UIDocument activo, o <c>null</c> si no hay documento abierto.</summary>
    UIDocument? ActiveUiDocument { get; }

    /// <summary>Document activo, o <c>null</c> si no hay documento abierto.</summary>
    Document? ActiveDocument { get; }

    /// <summary>True si hay un documento abierto.</summary>
    bool HasActiveDocument { get; }
}
