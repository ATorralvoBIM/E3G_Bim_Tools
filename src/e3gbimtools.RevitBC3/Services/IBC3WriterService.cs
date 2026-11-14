using System.Collections.Generic;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Exporta un BC3 (FIEBDC-3) a partir del mapeo del usuario, las mediciones
/// calculadas y opcionalmente un BC3 fuente del que tomar precios/descripciones.
/// </summary>
public interface IBC3WriterService
{
    /// <summary>
    /// Escribe un archivo BC3 con la estructura de capitulos definida por el
    /// usuario en la app y las mediciones calculadas.
    ///
    /// Sprint B: la jerarquia se toma de las entidades <see cref="Chapter"/> y
    /// <see cref="SubChapter"/> persistidas en el mapeo y de las FKs
    /// (<see cref="MappingRule.ChapterId"/>, <see cref="MappingRule.SubChapterId"/>).
    /// Las reglas sin ChapterId son "huerfanas" y no se incluyen en el BC3
    /// (AC3.3).  Los capitulos sin reglas con medicion no se emiten (AC1.3).
    /// El orden lo determina <see cref="Chapter.Order"/> / <see cref="SubChapter.Order"/>
    /// (AC5.2), no alfabetico.
    /// </summary>
    void Export(
        string outputPath,
        BC3Document? sourceBc3,
        IReadOnlyList<Chapter> chapters,
        IReadOnlyList<SubChapter> subChapters,
        IReadOnlyList<MappingRule> rules,
        IReadOnlyList<MeasurementResult> measurements,
        BC3ExportOptions options);
}

/// <summary>Opciones de exportacion.</summary>
public sealed class BC3ExportOptions
{
    /// <summary>Nombre del proyecto (raiz del BC3).</summary>
    public string ProjectName { get; set; } = "Presupuesto e3gbimtools";

    /// <summary>Emisor (programa que escribe el BC3).</summary>
    public string Emitter { get; set; } = "e3gbimtools.RevitBC3";

    /// <summary>Version de FIEBDC-3 declarada.</summary>
    /// <remarks>
    /// Mantener "FIEBDC-3/2024" alineado con el writer legacy.  Algunos
    /// importadores (notablemente el conversor BC3->TCQ nativo) usan rutas
    /// de codigo distintas segun la version declarada y rechazan partidas
    /// si el ~V dice una version anterior a la que esperan.
    /// </remarks>
    public string FormatVersion { get; set; } = "FIEBDC-3/2024";
}
