using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Services.Sections;

/// <summary>
/// Calcula bounding boxes minimos (Sprint 5).
/// Trabaja en unidades internas de Revit (pies).  El padding se recibe en
/// metros y se convierte aqui via <see cref="UnitUtils"/>.
/// </summary>
public sealed class BoundingBoxCalculator : IBoundingBoxCalculator
{
    private readonly ILogger _logger;

    public BoundingBoxCalculator(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<BoundingBoxCalculator>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public BoundingBoxXYZ Compute(
        Document doc,
        IReadOnlyList<Element> elements,
        double paddingMeters,
        bool includeLinks,
        SectionBoxAlignment alignment,
        Transform? alignmentTransform = null)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (elements is null) throw new ArgumentNullException(nameof(elements));

        var paddingFeet = UnitUtils.ConvertToInternalUnits(paddingMeters, UnitTypeId.Meters);

        // Si alignment == View y nos pasan un transform, calculamos una caja
        // ORIENTADA: proyectamos cada esquina al sistema de la vista (inversa del
        // transform), acumulamos min/max ahí y devolvemos el box con ese Transform.
        bool orientada = alignment == SectionBoxAlignment.View && alignmentTransform is not null;
        Transform frame = alignmentTransform ?? Transform.Identity;
        Transform inv = orientada ? frame.Inverse : Transform.Identity;

        XYZ? min = null, max = null;

        void AcumularBox(BoundingBoxXYZ bb)
        {
            // 8 esquinas del AABB del elemento (en mundo), proyectadas al frame.
            foreach (var corner in Corners(bb))
            {
                var p = orientada ? inv.OfPoint(corner) : corner;
                Expand(ref min, ref max, p, p);
            }
        }

        foreach (var elt in elements)
        {
            var bb = elt.get_BoundingBox(null);
            if (bb is not null) AcumularBox(bb);
        }

        if (includeLinks)
        {
            foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
            {
                if (link is not RevitLinkInstance li) continue;
                var bb = li.get_BoundingBox(null);
                if (bb is not null) AcumularBox(bb);
            }
        }

        if (min is null || max is null)
        {
            _logger.Warning("BoundingBoxCalculator: ningun elemento aporto bounding. Devolviendo box vacio.");
            return new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(0, 0, 0) };
        }

        var result = new BoundingBoxXYZ
        {
            Min = new XYZ(min.X - paddingFeet, min.Y - paddingFeet, min.Z - paddingFeet),
            Max = new XYZ(max.X + paddingFeet, max.Y + paddingFeet, max.Z + paddingFeet),
        };
        if (orientada) result.Transform = frame; // orienta la caja según la vista

        return result;
    }

    /// <summary>Las 8 esquinas (en mundo) de un AABB.</summary>
    private static IEnumerable<XYZ> Corners(BoundingBoxXYZ bb)
    {
        XYZ lo = bb.Min, hi = bb.Max;
        var t = bb.Transform ?? Transform.Identity;
        for (int xi = 0; xi < 2; xi++)
            for (int yi = 0; yi < 2; yi++)
                for (int zi = 0; zi < 2; zi++)
                    yield return t.OfPoint(new XYZ(xi == 0 ? lo.X : hi.X, yi == 0 ? lo.Y : hi.Y, zi == 0 ? lo.Z : hi.Z));
    }

    private static void Expand(ref XYZ? min, ref XYZ? max, XYZ candidateMin, XYZ candidateMax)
    {
        min = min is null ? candidateMin : new XYZ(
            Math.Min(min.X, candidateMin.X),
            Math.Min(min.Y, candidateMin.Y),
            Math.Min(min.Z, candidateMin.Z));

        max = max is null ? candidateMax : new XYZ(
            Math.Max(max.X, candidateMax.X),
            Math.Max(max.Y, candidateMax.Y),
            Math.Max(max.Z, candidateMax.Z));
    }
}
