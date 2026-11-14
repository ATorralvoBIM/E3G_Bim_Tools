using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using E3G.BimTools.RevitBC3.Mapping;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion de <see cref="IMappingPersistenceService"/> basada en
/// System.Text.Json (built-in en .NET 8, sin NuGet adicional).
///
/// Formatos soportados al cargar:
///  - V0 (legacy: Sprint A2 inicial):  array plano de MappingRule.
///  - V1 (legacy: Sprint A2):  objeto con Rules + LastBC3Path, sin
///        Chapters como entidad.
///  - V2 (Sprint B):  objeto con Chapters/SubChapters como entidades
///        de primera clase y FKs en las reglas.
///
/// Al guardar (<see cref="Save"/>) siempre escribe la version V2.
/// El export en formato legacy (V1) esta en <see cref="SaveLegacy"/>.
///
/// Migracion V0/V1 -> V2 (Sprint B, AC6.1): al cargar un fichero antiguo,
/// se reconstruyen <see cref="Chapter"/> y <see cref="SubChapter"/> a partir
/// de los strings unicos que tengan las reglas (un Chapter por
/// ChapterCode distinto; un SubChapter por (ChapterCode, SubChapterCode)).
/// El orden se asigna por el orden de aparicion en las reglas.  Las FKs
/// se rellenan en las reglas.
/// </summary>
public sealed class MappingPersistenceService : IMappingPersistenceService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string DefaultPath
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "e3g", "DiTools", "bc3-mapping");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "rules.json");
        }
    }

    public MappingFile Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new MappingFile();

        var json = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(json)) return new MappingFile();

        // Detecta formato V0: array plano (Sprint A2 inicial).
        var trimmed = json.TrimStart();
        if (trimmed.StartsWith("["))
        {
            var rules = JsonSerializer.Deserialize<List<MappingRule>>(json, Options)
                       ?? new List<MappingRule>();
            var file = new MappingFile
            {
                SchemaVersion = 2,
                LastBC3Path = string.Empty,
                Rules = rules,
            };
            MigrateLegacyToV2(file);
            return file;
        }

        // Formato V1/V2 (objeto).
        var loaded = JsonSerializer.Deserialize<MappingFile>(json, Options) ?? new MappingFile();

        // Migracion V1 -> V2 si no hay Chapters serializados (o estan vacios)
        // y, sin embargo, las reglas si traen strings de capitulo: hay que
        // sintetizar entidades.  Si Chapters esta vacio y las reglas tampoco
        // traen strings, simplemente queda un fichero vacio V2 limpio (no
        // hacemos nada).
        if (loaded.Chapters.Count == 0 && AnyRuleHasLegacyChapter(loaded.Rules))
        {
            MigrateLegacyToV2(loaded);
        }
        else
        {
            // Aunque haya chapters, blindamos consistencia: si alguna regla
            // tiene strings legacy pero no FKs (caso mixto raro), tambien
            // hay que enlazarla.
            BackfillMissingFks(loaded);
        }

        loaded.SchemaVersion = 2;
        return loaded;
    }

    public void Save(string filePath, MappingFile file)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Ruta vacia.", nameof(filePath));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        file ??= new MappingFile();
        file.SchemaVersion = 2;

        // Antes de serializar, mantenemos los strings legacy de las reglas
        // sincronizados con sus FKs.  Asi un fichero V2 sigue siendo legible
        // (parcialmente) por un loader antiguo: este solo veria los strings,
        // perderia FKs y orden, pero al menos no llegaria un mapeo "vacio".
        SyncLegacyStringsFromFks(file);

        var json = JsonSerializer.Serialize(file, Options);
        File.WriteAllText(filePath, json);
    }

    public void SaveLegacy(string filePath, MappingFile file)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Ruta vacia.", nameof(filePath));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        file ??= new MappingFile();

        // Snapshot legacy: sin Chapters/SubChapters, schema 1, las reglas
        // llevan los strings sincronizados desde las FKs.
        SyncLegacyStringsFromFks(file);

        var legacy = new MappingFile
        {
            SchemaVersion = 1,
            LastBC3Path = file.LastBC3Path,
            Chapters = new List<Chapter>(),     // vacio: formato legacy
            SubChapters = new List<SubChapter>(),
            Rules = file.Rules
                .Select(CloneRuleAsLegacy)
                .ToList(),
        };

        var json = JsonSerializer.Serialize(legacy, Options);
        File.WriteAllText(filePath, json);
    }

    // ============================================================
    // Migracion legacy -> V2
    // ============================================================

    private static bool AnyRuleHasLegacyChapter(IEnumerable<MappingRule> rules)
    {
        if (rules is null) return false;
        foreach (var r in rules)
        {
            if (r is null) continue;
            if (!string.IsNullOrWhiteSpace(r.ChapterCode)) return true;
            if (!string.IsNullOrWhiteSpace(r.ChapterName)) return true;
        }
        return false;
    }

    /// <summary>
    /// Crea Chapters/SubChapters derivados de los strings legacy
    /// (ChapterCode/Name, SubChapterCode/Name) que aparecen en las
    /// reglas y rellena las FKs en cada regla.  Las reglas sin
    /// ChapterCode quedan "huerfanas" (ChapterId = null, no se exportan).
    /// </summary>
    private static void MigrateLegacyToV2(MappingFile file)
    {
        var chapters = new List<Chapter>();
        var subChapters = new List<SubChapter>();

        // Clave para chapters: ChapterCode normalizado (trim).  Si dos
        // reglas traen mismo codigo pero nombres distintos, gana el primero
        // que aparece y se conserva.  El usuario puede renombrarlo despues.
        var chapterByCode = new Dictionary<string, Chapter>(StringComparer.Ordinal);

        // Clave para subchapters: (ChapterCode normalizado, SubChapterCode
        // normalizado).  Mismo subcap puede aparecer en varias reglas.
        var subByKey = new Dictionary<(string, string), SubChapter>();

        int chapterOrder = 0;
        var subOrderByChapter = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var rule in file.Rules)
        {
            if (rule is null) continue;

            var chCode = (rule.ChapterCode ?? string.Empty).Trim();
            var chName = (rule.ChapterName ?? string.Empty).Trim();

            // Regla sin ChapterCode ni ChapterName -> queda huerfana (sin FK).
            if (chCode.Length == 0 && chName.Length == 0)
            {
                rule.ChapterId = null;
                rule.SubChapterId = null;
                continue;
            }

            // Si solo hay ChapterName y no Code, usamos el name como clave
            // (caso raro pero sostenible).  Si solo hay Code, ese es la clave.
            var chKey = chCode.Length > 0 ? "code:" + chCode : "name:" + chName;

            if (!chapterByCode.TryGetValue(chKey, out var chapter))
            {
                chapter = new Chapter
                {
                    Code = chCode,
                    Name = chName.Length > 0 ? chName : chCode,
                    Order = chapterOrder++,
                };
                chapterByCode[chKey] = chapter;
                chapters.Add(chapter);
            }
            else
            {
                // Si el ChapterName se ha completado a posteriori, completalo.
                if (string.IsNullOrEmpty(chapter.Name) && chName.Length > 0)
                    chapter.Name = chName;
            }

            rule.ChapterId = chapter.Id;

            var subCode = (rule.SubChapterCode ?? string.Empty).Trim();
            var subName = (rule.SubChapterName ?? string.Empty).Trim();
            if (subCode.Length == 0 && subName.Length == 0)
            {
                rule.SubChapterId = null;
                continue;
            }

            var subKey = (chapter.Id, subCode.Length > 0 ? "code:" + subCode : "name:" + subName);
            if (!subByKey.TryGetValue(subKey, out var sub))
            {
                if (!subOrderByChapter.TryGetValue(chapter.Id, out var subOrder)) subOrder = 0;

                sub = new SubChapter
                {
                    ChapterId = chapter.Id,
                    Code = subCode,
                    Name = subName.Length > 0 ? subName : subCode,
                    Order = subOrder,
                };
                subByKey[subKey] = sub;
                subChapters.Add(sub);
                subOrderByChapter[chapter.Id] = subOrder + 1;
            }
            else
            {
                if (string.IsNullOrEmpty(sub.Name) && subName.Length > 0)
                    sub.Name = subName;
            }

            rule.SubChapterId = sub.Id;
        }

        file.Chapters = chapters;
        file.SubChapters = subChapters;
        file.SchemaVersion = 2;
    }

    /// <summary>
    /// Cuando un fichero llega con Chapters poblados pero alguna regla
    /// no tiene FKs (caso mixto / fichero editado a mano), intenta
    /// resolverlas por matching de los strings.
    /// </summary>
    private static void BackfillMissingFks(MappingFile file)
    {
        if (file.Chapters.Count == 0) return;

        var chByCode = file.Chapters
            .GroupBy(c => (c.Code ?? string.Empty).Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var subByCode = file.SubChapters
            .GroupBy(s => (s.ChapterId, (s.Code ?? string.Empty).Trim()))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var rule in file.Rules)
        {
            if (rule is null) continue;
            if (!string.IsNullOrEmpty(rule.ChapterId)) continue;

            var chCode = (rule.ChapterCode ?? string.Empty).Trim();
            if (chCode.Length == 0) continue;
            if (!chByCode.TryGetValue(chCode, out var ch)) continue;

            rule.ChapterId = ch.Id;

            var subCode = (rule.SubChapterCode ?? string.Empty).Trim();
            if (subCode.Length == 0) continue;
            if (subByCode.TryGetValue((ch.Id, subCode), out var sub))
                rule.SubChapterId = sub.Id;
        }
    }

    /// <summary>
    /// Sincroniza los strings legacy de cada regla con los valores actuales
    /// de su Chapter/SubChapter via FK.  Se llama justo antes de serializar
    /// (Save y SaveLegacy).
    /// </summary>
    private static void SyncLegacyStringsFromFks(MappingFile file)
    {
        var chById = file.Chapters
            .Where(c => !string.IsNullOrEmpty(c.Id))
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var subById = file.SubChapters
            .Where(s => !string.IsNullOrEmpty(s.Id))
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var rule in file.Rules)
        {
            if (rule is null) continue;

            if (!string.IsNullOrEmpty(rule.ChapterId)
                && chById.TryGetValue(rule.ChapterId, out var ch))
            {
                rule.ChapterCode = ch.Code ?? string.Empty;
                rule.ChapterName = ch.Name ?? string.Empty;
            }
            else
            {
                // Sin chapter (huerfana): limpiamos strings tambien.
                rule.ChapterCode = string.Empty;
                rule.ChapterName = string.Empty;
            }

            if (!string.IsNullOrEmpty(rule.SubChapterId)
                && subById.TryGetValue(rule.SubChapterId, out var sub))
            {
                rule.SubChapterCode = sub.Code ?? string.Empty;
                rule.SubChapterName = sub.Name ?? string.Empty;
            }
            else
            {
                rule.SubChapterCode = string.Empty;
                rule.SubChapterName = string.Empty;
            }
        }
    }

    private static MappingRule CloneRuleAsLegacy(MappingRule src)
    {
        // No clonamos profundamente Filters: por valor son inmutables en
        // contexto del save (no se mutan al escribir el JSON), y reutilizar
        // las referencias originales evita una copia innecesaria.
        return new MappingRule
        {
            Id = src.Id,
            Name = src.Name,
            ChapterId = null,         // formato legacy: sin FK
            SubChapterId = null,
            ChapterCode = src.ChapterCode,
            ChapterName = src.ChapterName,
            SubChapterCode = src.SubChapterCode,
            SubChapterName = src.SubChapterName,
            CatalogSummary = src.CatalogSummary,
            CatalogUnit = src.CatalogUnit,
            CatalogPrice = src.CatalogPrice,
            CatalogDescription = src.CatalogDescription,
            CategoryValue = src.CategoryValue,
            CategoryDisplayName = src.CategoryDisplayName,
            Filters = src.Filters,
            BC3Code = src.BC3Code,
            Measurement = src.Measurement,
            UnitFactor = src.UnitFactor,
            Priority = src.Priority,
            Enabled = src.Enabled,
            Notes = src.Notes,
        };
    }
}
