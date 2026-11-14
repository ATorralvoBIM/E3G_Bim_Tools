using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Calcula el bounding box minimo que envuelve una coleccion de elementos
/// con un padding configurable (Sprint 5).  Sin transacciones: solo lectura.
/// </summary>
public interface IBoundingBoxCalculator
{
    /// <summary>
    /// Calcula el bounding global de <paramref name="elements"/>, ampliado por
    /// <paramref name="paddingMeters"/> en cada eje.  Si <paramref name="includeLinks"/>
    /// es true incluye los bounding de los <c>RevitLinkInstance</c> contenidos.
    /// </summary>
    BoundingBoxXYZ Compute(
        Document doc,
        IReadOnlyList<Element> elements,
        double paddingMeters,
        bool includeLinks,
        SectionBoxAlignment alignment,
        Transform? alignmentTransform = null);
}
