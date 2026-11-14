using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Recorre el modelo Revit aplicando las reglas y devuelve las mediciones
/// acumuladas por partida BC3 + los elementos huerfanos.
/// </summary>
public interface IRevitDataExtractor
{
    /// <summary>
    /// Extrae mediciones del documento aplicando todas las reglas activas.
    /// Si <paramref name="bc3Document"/> esta disponible, las descripciones
    /// y unidades de las partidas resultantes se rellenan desde alli.
    /// </summary>
    ExtractionResult ExtractAll(Document doc, IReadOnlyList<MappingRule> rules, BC3Document? bc3Document = null);
}
