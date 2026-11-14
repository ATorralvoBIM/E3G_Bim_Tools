using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Resuelve la lista de elementos que entrara en el calculo del bounding
/// del SectionBoxer (Sprint 5).  Hay una implementacion por valor de
/// <see cref="SectionBoxSourceKind"/>.
/// </summary>
public interface ISectionBoxSource
{
    /// <summary>Tipo de origen que esta implementacion sabe resolver.</summary>
    SectionBoxSourceKind Kind { get; }

    /// <summary>
    /// Devuelve los elementos del documento que aporta este origen para el
    /// calculo.  Si el request es inconsistente devuelve lista vacia.
    /// </summary>
    IReadOnlyList<Element> Resolve(Document doc, SectionBoxRequest request);
}
