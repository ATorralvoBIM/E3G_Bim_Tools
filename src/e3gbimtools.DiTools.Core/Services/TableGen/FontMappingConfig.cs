using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Services.TableGen;

/// <summary>
/// Mapea un tamaño de fuente Excel (puntos) + bold + italic
/// al TextNoteType de Revit mas adecuado (Sprint 8 — TableGen).
///
/// Estrategia:
///   1. Convierte pt → pies (1 pt = 1/864 ft).
///   2. Busca el TextNoteType cuya altura sea la mas cercana.
///   3. Si existe una variante Bold/Italic del mismo tamaño, la usa.
///   4. Cachea el resultado para no recorrer el documento en cada celda.
/// </summary>
internal sealed class FontMappingConfig
{
    private readonly ElementId _defaultTypeId;
    private readonly List<(double HeightFt, bool Bold, bool Italic, ElementId Id)> _types;
    private readonly Dictionary<(double, bool, bool), ElementId> _cache = new();
    private readonly IList<string> _warnings;

    // 1 punto = 1/72 pulgada = 1/(72*12) pies
    private const double PtToFt = 1.0 / (72.0 * 12.0);

    public FontMappingConfig(Document doc, IList<string> warnings)
    {
        _warnings = warnings;

        var allTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .ToList();

        _types = allTypes
            .Select(t =>
            {
                var heightParam = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                double heightFt = heightParam?.AsDouble() ?? 0.01;
                bool bold   = t.Name.Contains("Bold",   StringComparison.OrdinalIgnoreCase);
                bool italic = t.Name.Contains("Italic", StringComparison.OrdinalIgnoreCase);
                return (heightFt, bold, italic, t.Id);
            })
            .ToList();

        // Tipo por defecto: el primero disponible o InvalidElementId.
        _defaultTypeId = _types.Count > 0
            ? _types[0].Id
            : ElementId.InvalidElementId;
    }

    /// <summary>
    /// Devuelve el <see cref="ElementId"/> del TextNoteType mas adecuado
    /// para la fuente indicada.
    /// </summary>
    public ElementId GetTextNoteTypeId(double fontSizePt, bool bold, bool italic)
    {
        var key = (Math.Round(fontSizePt, 1), bold, italic);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        if (_types.Count == 0)
        {
            _warnings.Add("No se encontraron TextNoteTypes en el documento; usando tipo por defecto.");
            _cache[key] = _defaultTypeId;
            return _defaultTypeId;
        }

        double targetFt = fontSizePt * PtToFt;

        // 1) Buscar candidato con bold/italic exacto.
        var best = FindClosest(targetFt, bold, italic);

        // 2) Si no hay coincidencia exact de estilo, relajar.
        if (best == ElementId.InvalidElementId && (bold || italic))
        {
            best = FindClosest(targetFt, false, false);
            if (best != ElementId.InvalidElementId)
                _warnings.Add(
                    $"No se encontro TextNoteType Bold={bold}/Italic={italic} para " +
                    $"{fontSizePt:F1} pt; usando el tipo base mas cercano.");
        }

        if (best == ElementId.InvalidElementId)
            best = _defaultTypeId;

        _cache[key] = best;
        return best;
    }

    private ElementId FindClosest(double targetFt, bool bold, bool italic)
    {
        ElementId bestId = ElementId.InvalidElementId;
        double bestDist  = double.MaxValue;

        foreach (var (ht, b, it, id) in _types)
        {
            if (b != bold || it != italic) continue;
            double dist = Math.Abs(ht - targetFt);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestId   = id;
            }
        }

        return bestId;
    }
}
