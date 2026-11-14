// Sprint B - Tests de migracion V0/V1 -> V2 y exports legacy.
//
// Cubren:
//   1. Cargar un mapping legacy V1 (sin Chapters/SubChapters como entidades)
//      y verificar que MappingPersistenceService.Load() crea Chapters y
//      SubChapters derivados de los strings de cada regla y asigna las FKs.
//   2. Cargar un mapping legacy V0 (array plano de reglas) y mismo
//      tratamiento.
//   3. Round-trip writer: BC3 generado desde un mapping legacy migrado
//      debe contener las mismas partidas en los mismos capitulos que el
//      writer antiguo habria producido.
//   4. SaveLegacy: el mapeo V2 vuelve a salir como V1 con strings
//      sincronizados desde las FKs.
//   5. Una regla sin ChapterId ("huerfana") NO aparece en el BC3
//      exportado (AC3.3) y deja capitulos sin reglas tambien fuera del
//      BC3 final (AC1.3).
//   6. Capitulos vacios (sin reglas) se omiten del BC3.
//   7. Orden de capitulos: el writer respeta Chapter.Order, no el orden
//      alfabetico del Code (AC5.2).
//
// No requieren RevitAPI: trabajamos solo con Mapping y Services.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;
using E3G.BimTools.RevitBC3.Services;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.RevitBC3;

public sealed class MappingMigrationTests
{
    private static MappingRule MakeLegacyRule(
        string id,
        string chapterCode,
        string chapterName,
        string subCode,
        string subName,
        string bc3Code,
        string ruleName = "")
        => new()
        {
            Id = id,
            Name = ruleName,
            ChapterCode = chapterCode,
            ChapterName = chapterName,
            SubChapterCode = subCode,
            SubChapterName = subName,
            BC3Code = bc3Code,
            Enabled = true,
            Priority = 100,
            UnitFactor = 1.0,
        };

    // ============================================================
    //  Migracion V1 (objeto sin Chapters) -> V2
    // ============================================================

    [Fact]
    public void Load_LegacyV1_CreaChaptersYSubChapters_YAsignaFks()
    {
        var legacyJson = @"{
  ""SchemaVersion"": 1,
  ""LastBC3Path"": """",
  ""Rules"": [
    { ""Id"": ""r1"", ""ChapterCode"": ""01"", ""ChapterName"": ""SANEAMIENTO"", ""SubChapterCode"": ""AFS"", ""SubChapterName"": ""Aigua Freda"", ""BC3Code"": ""ED15N711.001"", ""Enabled"": true, ""Priority"": 100, ""UnitFactor"": 1.0, ""CategoryValue"": -2008049 },
    { ""Id"": ""r2"", ""ChapterCode"": ""01"", ""ChapterName"": ""SANEAMIENTO"", ""SubChapterCode"": ""PLU"", ""SubChapterName"": ""Pluviales"",   ""BC3Code"": ""ED15B873"",     ""Enabled"": true, ""Priority"": 100, ""UnitFactor"": 1.0, ""CategoryValue"": -2008049 },
    { ""Id"": ""r3"", ""ChapterCode"": ""02"", ""ChapterName"": ""BAJANTES"",    ""SubChapterCode"": """",    ""SubChapterName"": """",            ""BC3Code"": ""EFA12345"",      ""Enabled"": true, ""Priority"": 100, ""UnitFactor"": 1.0, ""CategoryValue"": -2008049 }
  ]
}";
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, legacyJson);
        try
        {
            var svc = new MappingPersistenceService();
            var loaded = svc.Load(tmp);

            loaded.SchemaVersion.Should().Be(2);
            loaded.Chapters.Should().HaveCount(2);
            loaded.Chapters.Should().Contain(c => c.Code == "01" && c.Name == "SANEAMIENTO");
            loaded.Chapters.Should().Contain(c => c.Code == "02" && c.Name == "BAJANTES");
            loaded.SubChapters.Should().HaveCount(2);   // AFS y PLU (02 no tiene sub)

            var ch01 = loaded.Chapters.Single(c => c.Code == "01");
            var ch02 = loaded.Chapters.Single(c => c.Code == "02");
            var afs  = loaded.SubChapters.Single(s => s.Code == "AFS");
            var plu  = loaded.SubChapters.Single(s => s.Code == "PLU");

            afs.ChapterId.Should().Be(ch01.Id);
            plu.ChapterId.Should().Be(ch01.Id);

            loaded.Rules.Should().HaveCount(3);
            loaded.Rules.Single(r => r.Id == "r1").ChapterId.Should().Be(ch01.Id);
            loaded.Rules.Single(r => r.Id == "r1").SubChapterId.Should().Be(afs.Id);
            loaded.Rules.Single(r => r.Id == "r2").ChapterId.Should().Be(ch01.Id);
            loaded.Rules.Single(r => r.Id == "r2").SubChapterId.Should().Be(plu.Id);
            loaded.Rules.Single(r => r.Id == "r3").ChapterId.Should().Be(ch02.Id);
            loaded.Rules.Single(r => r.Id == "r3").SubChapterId.Should().BeNull();
        }
        finally { File.Delete(tmp); }
    }

    // ============================================================
    //  Migracion V0 (array plano) -> V2
    // ============================================================

    [Fact]
    public void Load_LegacyV0_ArrayPlano_MigraAV2()
    {
        var legacyJson = @"[
  { ""Id"": ""r1"", ""ChapterCode"": ""01"", ""ChapterName"": ""SANEAMIENTO"", ""SubChapterCode"": ""AFS"", ""SubChapterName"": ""Aigua Freda"", ""BC3Code"": ""ED15N711.001"", ""Enabled"": true, ""Priority"": 100, ""UnitFactor"": 1.0, ""CategoryValue"": -2008049 }
]";
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, legacyJson);
        try
        {
            var svc = new MappingPersistenceService();
            var loaded = svc.Load(tmp);

            loaded.SchemaVersion.Should().Be(2);
            loaded.Chapters.Should().HaveCount(1);
            loaded.Chapters[0].Code.Should().Be("01");
            loaded.SubChapters.Should().HaveCount(1);
            loaded.SubChapters[0].Code.Should().Be("AFS");
            loaded.Rules.Single().ChapterId.Should().Be(loaded.Chapters[0].Id);
            loaded.Rules.Single().SubChapterId.Should().Be(loaded.SubChapters[0].Id);
        }
        finally { File.Delete(tmp); }
    }

    // ============================================================
    //  Round-trip: V1 cargado migrado -> Save V2 -> Load V2.
    // ============================================================

    [Fact]
    public void Save_V2_PreservaChaptersFks_AlRecargar()
    {
        // 1) Cargar V1.
        var legacyJson = @"{
  ""SchemaVersion"": 1,
  ""LastBC3Path"": """",
  ""Rules"": [
    { ""Id"": ""r1"", ""ChapterCode"": ""01"", ""ChapterName"": ""SAN"", ""BC3Code"": ""ABC"", ""Enabled"": true, ""Priority"": 100, ""UnitFactor"": 1.0, ""CategoryValue"": -2008049 }
  ]
}";
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, legacyJson);
        try
        {
            var svc = new MappingPersistenceService();
            var loaded = svc.Load(tmp);
            var chapterIdAfterLoad = loaded.Rules[0].ChapterId;

            // 2) Guardar (formato V2).
            svc.Save(tmp, loaded);

            // 3) Recargar y verificar que la FK persiste.
            var reload = svc.Load(tmp);
            reload.SchemaVersion.Should().Be(2);
            reload.Chapters.Should().HaveCount(1);
            reload.Chapters[0].Id.Should().Be(chapterIdAfterLoad!);
            reload.Rules[0].ChapterId.Should().Be(chapterIdAfterLoad!);
            reload.Rules[0].ChapterCode.Should().Be("01");   // strings sincronizados desde FK
            reload.Rules[0].ChapterName.Should().Be("SAN");
        }
        finally { File.Delete(tmp); }
    }

    // ============================================================
    //  SaveLegacy: exporta en V1 sin Chapters como entidades.
    // ============================================================

    [Fact]
    public void SaveLegacy_EscribeV1_SinChaptersComoEntidades_YStringsSincronizados()
    {
        var file = new MappingFile
        {
            SchemaVersion = 2,
            Chapters = new List<Chapter>
            {
                new() { Id = "ch1", Code = "01", Name = "SAN", Order = 0 },
            },
            SubChapters = new List<SubChapter>
            {
                new() { Id = "sub1", ChapterId = "ch1", Code = "AFS", Name = "Aigua Freda", Order = 0 },
            },
            Rules = new List<MappingRule>
            {
                new()
                {
                    Id = "r1",
                    ChapterId = "ch1",
                    SubChapterId = "sub1",
                    BC3Code = "ABC",
                    Enabled = true,
                    Priority = 100,
                    UnitFactor = 1.0,
                },
            },
        };

        var tmp = Path.GetTempFileName();
        try
        {
            var svc = new MappingPersistenceService();
            svc.SaveLegacy(tmp, file);

            var text = File.ReadAllText(tmp);
            text.Should().Contain("\"SchemaVersion\": 1");
            // Los strings de capitulo se sincronizan desde la entidad antes de escribir.
            text.Should().Contain("\"ChapterCode\": \"01\"");
            text.Should().Contain("\"ChapterName\": \"SAN\"");
            text.Should().Contain("\"SubChapterCode\": \"AFS\"");
            // No deberian aparecer Chapters/SubChapters como propiedades pobladas
            // (la serializacion omite las propiedades por nulos/vacios).  El
            // formato V1 cargable por loaders antiguos no las tendria.  STJ con
            // WhenWritingNull no omite arrays vacios — los emite como [].
            text.Should().Contain("\"Chapters\": []");
            text.Should().Contain("\"SubChapters\": []");
        }
        finally { File.Delete(tmp); }
    }

    // ============================================================
    //  Writer: regla huerfana (sin ChapterId) NO se exporta.
    // ============================================================

    [Fact]
    public void Writer_NoIncluye_ReglaHuerfana_EnElBC3()
    {
        var chapters = new List<Chapter>
        {
            new() { Id = "ch1", Code = "01", Name = "SAN", Order = 0 },
        };
        var subs = new List<SubChapter>();
        var rules = new List<MappingRule>
        {
            new() { Id = "r-asignada", ChapterId = "ch1", BC3Code = "AAA", Enabled = true, Priority = 100, UnitFactor = 1.0 },
            new() { Id = "r-huerfana", ChapterId = null,  BC3Code = "BBB", Enabled = true, Priority = 100, UnitFactor = 1.0 },
        };
        var measurements = new List<MeasurementResult>
        {
            new() { BC3Code = "AAA", Quantity = 10.0, Unit = "m", ContributingRuleIds = new List<string> { "r-asignada" } },
            new() { BC3Code = "BBB", Quantity = 5.0,  Unit = "m", ContributingRuleIds = new List<string> { "r-huerfana" } },
        };

        var tmp = Path.GetTempFileName() + ".bc3";
        try
        {
            var writer = new BC3WriterService();
            writer.Export(tmp, sourceBc3: null,
                chapters: chapters, subChapters: subs, rules: rules,
                measurements: measurements, options: new BC3ExportOptions());

            var content = File.ReadAllText(tmp, System.Text.Encoding.GetEncoding(1252));
            content.Should().Contain("|AAA|");
            content.Should().NotContain("|BBB|");
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    // ============================================================
    //  Writer: capitulo sin reglas se omite del BC3.
    // ============================================================

    [Fact]
    public void Writer_OmiteCapituloSinReglas()
    {
        var chapters = new List<Chapter>
        {
            new() { Id = "ch1", Code = "01", Name = "SAN", Order = 0 },
            new() { Id = "ch2", Code = "02", Name = "ELE", Order = 1 },   // vacio
        };
        var rules = new List<MappingRule>
        {
            new() { Id = "r1", ChapterId = "ch1", BC3Code = "AAA", Enabled = true, Priority = 100, UnitFactor = 1.0 },
        };
        var measurements = new List<MeasurementResult>
        {
            new() { BC3Code = "AAA", Quantity = 1.0, Unit = "m", ContributingRuleIds = new List<string> { "r1" } },
        };

        var tmp = Path.GetTempFileName() + ".bc3";
        try
        {
            var writer = new BC3WriterService();
            writer.Export(tmp, sourceBc3: null,
                chapters: chapters, subChapters: new List<SubChapter>(),
                rules: rules, measurements: measurements,
                options: new BC3ExportOptions { ProjectName = "Test" });

            var content = File.ReadAllText(tmp, System.Text.Encoding.GetEncoding(1252));
            content.Should().Contain("|01##|");
            content.Should().NotContain("|02##|");   // no se emite el capitulo vacio
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    // ============================================================
    //  Writer: el orden del BC3 respeta Chapter.Order, no alfabetico.
    // ============================================================

    [Fact]
    public void Writer_RespetaOrden_DeChapterOrder_NoAlfabetico()
    {
        // Capitulo Z con Order 0, Capitulo A con Order 1.  El writer debe
        // emitir Z antes que A (el usuario ha elegido ese orden).
        var chapters = new List<Chapter>
        {
            new() { Id = "chZ", Code = "Z", Name = "Zeta", Order = 0 },
            new() { Id = "chA", Code = "A", Name = "Alfa", Order = 1 },
        };
        var rules = new List<MappingRule>
        {
            new() { Id = "rZ", ChapterId = "chZ", BC3Code = "PARTIDAZ", Enabled = true, Priority = 100, UnitFactor = 1.0 },
            new() { Id = "rA", ChapterId = "chA", BC3Code = "PARTIDAA", Enabled = true, Priority = 100, UnitFactor = 1.0 },
        };
        var measurements = new List<MeasurementResult>
        {
            new() { BC3Code = "PARTIDAZ", Quantity = 1.0, Unit = "m", ContributingRuleIds = new List<string> { "rZ" } },
            new() { BC3Code = "PARTIDAA", Quantity = 1.0, Unit = "m", ContributingRuleIds = new List<string> { "rA" } },
        };

        var tmp = Path.GetTempFileName() + ".bc3";
        try
        {
            var writer = new BC3WriterService();
            writer.Export(tmp, sourceBc3: null,
                chapters: chapters, subChapters: new List<SubChapter>(),
                rules: rules, measurements: measurements,
                options: new BC3ExportOptions());

            var content = File.ReadAllText(tmp, System.Text.Encoding.GetEncoding(1252));
            // El ~D del proyecto raiz lista los capitulos en el orden del
            // export.  Buscamos la posicion de "|Z##|" y "|A##|" en el ~D
            // del root.
            int idxRoot = content.IndexOf("~D|##|", System.StringComparison.Ordinal);
            idxRoot.Should().BeGreaterThan(0);
            var lineRoot = content.Substring(idxRoot, System.Math.Min(120, content.Length - idxRoot));
            var idxZ = lineRoot.IndexOf("Z##", System.StringComparison.Ordinal);
            var idxA = lineRoot.IndexOf("A##", System.StringComparison.Ordinal);
            idxZ.Should().BeGreaterThan(0);
            idxA.Should().BeGreaterThan(idxZ, "Z (Order=0) debe aparecer antes que A (Order=1)");
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    // ============================================================
    //  Round-trip "viejo BC3 vs nuevo BC3 desde mapping migrado".
    //  Lo importante es que las MISMAS partidas viven bajo el MISMO
    //  capitulo y subcapitulo.  No comparamos byte-a-byte (la fecha
    //  varia) pero si las partidas que aparecen.
    // ============================================================

    [Fact]
    public void RoundTrip_LegacyMigradoProduceMismaJerarquia_DePartidas()
    {
        // Mapping V1: dos reglas en capitulo 01 / subcapitulo AFS.
        var legacyJson = @"{
  ""SchemaVersion"": 1,
  ""LastBC3Path"": """",
  ""Rules"": [
    { ""Id"": ""r1"", ""ChapterCode"": ""01"", ""ChapterName"": ""SAN"", ""SubChapterCode"": ""AFS"", ""SubChapterName"": ""Aigua"", ""BC3Code"": ""AAA"", ""Enabled"": true, ""Priority"": 100, ""UnitFactor"": 1.0, ""CategoryValue"": -2008049 },
    { ""Id"": ""r2"", ""ChapterCode"": ""01"", ""ChapterName"": ""SAN"", ""SubChapterCode"": ""AFS"", ""SubChapterName"": ""Aigua"", ""BC3Code"": ""BBB"", ""Enabled"": true, ""Priority"": 100, ""UnitFactor"": 1.0, ""CategoryValue"": -2008049 }
  ]
}";
        var tmpJson = Path.GetTempFileName();
        File.WriteAllText(tmpJson, legacyJson);
        var tmpBc3  = Path.GetTempFileName() + ".bc3";
        try
        {
            var svc = new MappingPersistenceService();
            var file = svc.Load(tmpJson);

            // El mapping V1 cargado debe tener Chapters/SubChapters generados.
            file.Chapters.Should().HaveCount(1);
            file.SubChapters.Should().HaveCount(1);

            var measurements = new List<MeasurementResult>
            {
                new() { BC3Code = "AAA", Quantity = 10.0, Unit = "m", ContributingRuleIds = new List<string> { "r1" } },
                new() { BC3Code = "BBB", Quantity = 5.0,  Unit = "m", ContributingRuleIds = new List<string> { "r2" } },
            };

            var writer = new BC3WriterService();
            writer.Export(tmpBc3, sourceBc3: null,
                chapters: file.Chapters, subChapters: file.SubChapters,
                rules: file.Rules, measurements: measurements,
                options: new BC3ExportOptions());

            var bc3 = File.ReadAllText(tmpBc3, System.Text.Encoding.GetEncoding(1252));
            bc3.Should().Contain("|01##|");
            bc3.Should().Contain("|01.AFS#|");
            bc3.Should().Contain("|AAA|");
            bc3.Should().Contain("|BBB|");
            // El ~D del subcap debe agrupar ambas partidas (formato:
            //   ~D|01.AFS#|AAA\1\10\BBB\1\5\| ).
            bc3.Should().MatchRegex(@"~D\|01\.AFS#\|.*AAA.*BBB.*\|");
        }
        finally
        {
            if (File.Exists(tmpJson)) File.Delete(tmpJson);
            if (File.Exists(tmpBc3))  File.Delete(tmpBc3);
        }
    }
}
