using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Wrapper sobre <c>UIDocument.Selection</c> para evitar que cada herramienta
/// del modulo replique el codigo de seleccion / filtrado.
/// </summary>
public interface ISelectionService
{
    /// <summary>IDs actualmente seleccionados en el documento activo.</summary>
    IReadOnlyList<ElementId> CurrentIds();

    /// <summary>Elementos actualmente seleccionados (resueltos a <see cref="Element"/>).</summary>
    IReadOnlyList<Element> CurrentElements();

    /// <summary>Solo los elementos seleccionados de tipo <typeparamref name="T"/>.</summary>
    IReadOnlyList<T> CurrentOfType<T>() where T : Element;

    /// <summary>Pide al usuario que seleccione elementos en la vista.</summary>
    IReadOnlyList<Element> PickObjects(string prompt, Predicate<Element>? filter = null);

    /// <summary>Sustituye la seleccion actual.</summary>
    void Set(IEnumerable<ElementId> ids);

    /// <summary>Limpia la seleccion.</summary>
    void Clear();
}
