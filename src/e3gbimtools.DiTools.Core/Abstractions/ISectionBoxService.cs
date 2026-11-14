using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Servicio principal del SectionBoxer (Sprint 5).
/// Toda escritura se agrupa en una unica Transaction descriptiva.
/// </summary>
public interface ISectionBoxService
{
    /// <summary>
    /// Aplica el section box derivado de <paramref name="request"/> a la vista
    /// <paramref name="target"/>.  Si <paramref name="target"/> es null y el
    /// request pide <c>CreateDedicatedView</c>, crea una vista 3D nueva con
    /// ese nombre.  En caso contrario falla con un Summary explicativo.
    /// </summary>
    SectionBoxResult Apply(Document doc, SectionBoxRequest request, View3D? target);

    /// <summary>
    /// Limpia el section box de <paramref name="view"/> dejando la vista en
    /// el estado previo guardado en memoria (mismo proceso).  Si no hay
    /// snapshot previo simplemente desactiva el section box.
    /// </summary>
    SectionBoxResult Clear(View3D view);
}
