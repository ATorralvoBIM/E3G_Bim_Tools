using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion de <see cref="IBimlinkExporter"/>.
///
/// Genera un archivo JSON con extension ".bimlink.json" en la misma carpeta
/// que el BC3 exportado, con el esquema definido en
/// bc3/FORMATO_VINCULACION_TCQ.md (Sprint A2).
///
/// Formato del nombre: &lt;nombre-bc3-sin-extension&gt;.bimlink.json
/// Encoding: UTF-8 con BOM (para compatibilidad con herramientas que lo
/// inspeccionan directamente como texto).
///
/// No requiere NuGet: usa System.Text.Json incluido en .NET 8.
/// </summary>
public sealed class BimlinkExporter : IBimlinkExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <inheritdoc/>
    public string Export(
        string bc3OutputPath,
        IReadOnlyList<MeasurementResult> measurements,
        IReadOnlyList<MappingRule> rules,
        IReadOnlyList<Chapter> chapters,
        IReadOnlyList<SubChapter> subChapters,
        ExtractionResult? extraction,
        string? ifcModelPath = null)
    {
        if (string.IsNullOrWhiteSpace(bc3OutputPath))
            throw new ArgumentException("Ruta del BC3 no puede estar vacia.", nameof(bc3OutputPath));

        chapters ??= Array.Empty<Chapter>();
        subChapters ??= Array.Empty<SubChapter>();

        // Derivar ruta del .bimlink.json a partir del .bc3
        var bimlinkPath = Path.ChangeExtension(bc3OutputPath, null) + ".bimlink.json";

        // Indice de reglas para resolver capitulo/subcapitulo por RuleId.
        // Tolera duplicados de Id (no deberia haber, pero blindarse).
        var rulesById = rules
            .Where(r => !string.IsNullOrEmpty(r.Id))
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Sprint B: indices Chapter/SubChapter por Id para resolver FKs.
        var chaptersById = chapters
            .Where(c => !string.IsNullOrEmpty(c.Id))
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var subsById = subChapters
            .Where(s => !string.IsNullOrEmpty(s.Id))
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Solo partidas con medicion real > 0 (igual que BC3WriterService)
        var nonZero = measurements
            .Where(m => !string.IsNullOrWhiteSpace(m.BC3Code) && m.FinalQuantity > 0)
            .ToList();

        // Detectar colisiones del codigo de catalogo (mismo codigo en varias reglas).
        // Debe replicar bit a bit la logica de BC3WriterService para que el
        // Codigo del bimlink coincida con el ~C del .bc3 (validacion V2 de
        // BimLinkingValidationTests).
        var codeUsageCount = nonZero
            .GroupBy(m => m.BC3Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var codeCounters = new Dictionary<string, int>(StringComparer.Ordinal);

        var partidas = new List<BimLinkPartida>(nonZero.Count);

        foreach (var m in nonZero)
        {
            // Resolver regla origen para obtener capitulo/subcapitulo
            MappingRule? rule = null;
            if (m.ContributingRuleIds is { Count: > 0 })
            {
                foreach (var id in m.ContributingRuleIds)
                {
                    if (rulesById.TryGetValue(id, out var r)) { rule = r; break; }
                }
            }
            rule ??= rules.FirstOrDefault(r => r.Enabled && r.BC3Code == m.BC3Code);

            // Codigo exportado = codigo del catalogo, opcionalmente sufijado
            // (.001, .002...) si varias reglas comparten codigo de catalogo.
            var originalCode = m.BC3Code;
            bool needsSuffix = codeUsageCount.TryGetValue(originalCode, out var count) && count > 1;
            string exportCode;
            if (needsSuffix)
            {
                codeCounters.TryGetValue(originalCode, out var seq);
                seq++;
                codeCounters[originalCode] = seq;
                exportCode = $"{originalCode}.{seq:D3}";
            }
            else
            {
                exportCode = originalCode;
            }

            // Construir elementos
            var elementos = new List<BimLinkElement>();
            if (!m.HasOverride && m.ElementContributions is { Count: > 0 })
            {
                foreach (var c in m.ElementContributions)
                {
                    if (c.Quantity <= 0) continue;
                    elementos.Add(new BimLinkElement
                    {
                        IfcGuid     = string.IsNullOrWhiteSpace(c.IfcGuid)
                                        ? "E" + c.ElementId.ToString(CultureInfo.InvariantCulture)
                                        : c.IfcGuid,
                        OrigenGuid  = c.IfcGuidSource.ToString(),   // "Parameter" | "Calculated" | "Fallback"
                        ElementId   = c.ElementId,
                        Cantidad    = c.Quantity,
                        SystemName  = c.SystemName ?? string.Empty,
                        LevelName   = c.LevelName  ?? string.Empty,
                    });
                }
            }

            // Sprint B: resolver Capitulo y Subcapitulo via FK si existen,
            // con fallback a los strings legacy del propio MappingRule (que
            // estan sincronizados al guardar el mapeo).  Una regla huerfana
            // (sin ChapterId) seguiria figurando aqui con capitulo "" — pero
            // el BC3 NO la incluye, asi que para coherencia mejor saltarla:
            // si BC3WriterService no la exporto, este bimlink tampoco.
            string capitulo = string.Empty;
            string subcapitulo = string.Empty;
            if (rule is not null
                && !string.IsNullOrEmpty(rule.ChapterId)
                && chaptersById.TryGetValue(rule.ChapterId, out var ch))
            {
                capitulo = ch.Code ?? string.Empty;
                if (!string.IsNullOrEmpty(rule.SubChapterId)
                    && subsById.TryGetValue(rule.SubChapterId, out var sub))
                {
                    subcapitulo = sub.Code ?? string.Empty;
                }
            }
            else
            {
                // Fallback estricto a los strings legacy de la regla por
                // compatibilidad (no deberia hacer falta en V2).
                capitulo = rule?.ChapterCode?.Trim() ?? string.Empty;
                subcapitulo = rule?.SubChapterCode?.Trim() ?? string.Empty;
            }

            // AC3.3 Sprint B: una regla huerfana (sin chapter resuelto y sin
            // capitulo legacy) no se exporta al BC3, asi que tampoco aparece
            // en el bimlink (mantenemos paridad uno-a-uno).
            if (rule is not null && string.IsNullOrEmpty(rule.ChapterId)
                && string.IsNullOrEmpty(capitulo))
            {
                continue;
            }

            partidas.Add(new BimLinkPartida
            {
                Codigo           = exportCode,
                CodigoCatalogo   = originalCode,
                Resumen          = m.Description ?? string.Empty,
                Unidad           = m.Unit ?? string.Empty,
                Cantidad         = m.FinalQuantity,
                EsOverrideManual = m.HasOverride,
                Capitulo         = capitulo,
                Subcapitulo      = subcapitulo,
                Elementos        = elementos,
            });
        }

        // Estadisticas de cobertura de GUID (desde ExtractionResult o calculadas desde contribuciones)
        var stats = BuildStats(extraction, partidas);

        var doc = new BimLinkDocument
        {
            Generated       = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Bc3File         = Path.GetFileName(bc3OutputPath),
            IfcModel        = !string.IsNullOrWhiteSpace(ifcModelPath)
                                ? Path.GetFileName(ifcModelPath)
                                : string.Empty,
            Partidas        = partidas,
            EstadisticasGuid = stats,
        };

        // Serializar y escribir con UTF-8 + BOM
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        File.WriteAllText(bimlinkPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        return bimlinkPath;
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static BimLinkGuidStats BuildStats(ExtractionResult? extraction, List<BimLinkPartida> partidas)
    {
        if (extraction is not null)
        {
            // Usar los contadores del ExtractionResult (mas precisos: incluyen todos los
            // elementos matcheados, no solo los de partidas con FinalQuantity > 0)
            return new BimLinkGuidStats
            {
                TotalElementos  = extraction.ElementsMatched,
                DesdeParametro  = extraction.GuidFromParameter,
                DesdeCalculo    = extraction.GuidCalculated,
                SinGuid         = extraction.GuidFallback,
                CoberturaPorc   = extraction.GuidCoveragePercent,
            };
        }

        // Fallback: calcular desde las contribuciones de las partidas del bimlink
        int param = 0, calc = 0, fallback = 0;
        foreach (var p in partidas)
        {
            foreach (var e in p.Elementos)
            {
                switch (e.OrigenGuid)
                {
                    case "Parameter":  param++;   break;
                    case "Calculated": calc++;    break;
                    default:           fallback++; break;
                }
            }
        }
        int total = param + calc + fallback;
        return new BimLinkGuidStats
        {
            TotalElementos = total,
            DesdeParametro = param,
            DesdeCalculo   = calc,
            SinGuid        = fallback,
            CoberturaPorc  = total == 0 ? 0.0 : (param + calc) * 100.0 / total,
        };
    }
}
