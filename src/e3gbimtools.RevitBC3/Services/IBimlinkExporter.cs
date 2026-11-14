using System.Collections.Generic;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Genera el archivo de vinculacion BIM (.bimlink.json) que acompana al BC3 exportado.
///
/// El archivo se graba en la misma carpeta que el BC3, con el mismo nombre base
/// y extension ".bimlink.json".
///
/// Ejemplo: si el BC3 se llama "presupuesto.bc3", el archivo de vinculacion
/// se llama "presupuesto.bimlink.json".
/// </summary>
public interface IBimlinkExporter
{
    /// <summary>
    /// Genera y escribe el archivo .bimlink.json junto al BC3 exportado.
    /// </summary>
    /// <param name="bc3OutputPath">Ruta completa del archivo BC3 que se acaba de exportar.</param>
    /// <param name="measurements">Lista de mediciones del presupuesto (con contribuciones de elementos).</param>
    /// <param name="rules">Lista de reglas de mapeo activas (para resolver capítulo/subcapitulo via FK).</param>
    /// <param name="chapters">Capitulos del mapeo (Sprint B: para resolver Chapter.Code via FK).</param>
    /// <param name="subChapters">Subcapitulos del mapeo (Sprint B: para resolver SubChapter.Code via FK).</param>
    /// <param name="extraction">Resultado completo de la extraccion (para estadisticas de GUID).</param>
    /// <param name="ifcModelPath">
    /// Nombre o ruta del archivo IFC de referencia (opcional). Si se proporciona, se incluye
    /// en el campo <c>IfcModel</c> del .bimlink.json para trazar qué IFC corresponde a este BC3.
    /// </param>
    /// <returns>Ruta del archivo .bimlink.json generado.</returns>
    string Export(
        string bc3OutputPath,
        IReadOnlyList<MeasurementResult> measurements,
        IReadOnlyList<MappingRule> rules,
        IReadOnlyList<Chapter> chapters,
        IReadOnlyList<SubChapter> subChapters,
        ExtractionResult? extraction,
        string? ifcModelPath = null);
}
