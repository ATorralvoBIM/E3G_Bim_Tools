// Sprint A — Tests del MappingRuleValidator.
//
// Cubren los cuatro casos pedidos en el briefing:
//   1) Dos reglas, distinto capitulo, mismo codigo  -> NO conflicto.
//   2) Dos reglas, mismo capitulo, mismo codigo, una deshabilitada -> NO conflicto.
//   3) Dos reglas, mismo capitulo, mismo codigo, ambas habilitadas -> conflicto.
//   4) Dos reglas, mismo capitulo+subcap, mismo codigo -> conflicto.
//
// Tests adicionales de robustez: reglas con BC3Code vacio (no cuentan),
// auto-deteccion (FindConflictFor excluye al candidato por Id), y un
// caso con tres reglas que agrupan 3 ids en el mismo RuleConflict.

using System.Linq;
using E3G.BimTools.RevitBC3.Mapping;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.RevitBC3;

public sealed class MappingRuleValidatorTests
{
    private static MappingRule MakeRule(
        string id,
        string chapter,
        string subChapter,
        string bc3,
        bool enabled = true)
        => new()
        {
            Id = id,
            ChapterCode = chapter,
            ChapterName = chapter == "" ? "" : "Cap " + chapter,
            SubChapterCode = subChapter,
            SubChapterName = subChapter == "" ? "" : "Sub " + subChapter,
            BC3Code = bc3,
            Enabled = enabled,
        };

    // ============================================================
    //  DetectConflicts
    // ============================================================

    [Fact]
    public void DetectConflicts_DistintoCapitulo_MismoCodigo_NoEsConflicto()
    {
        var rules = new[]
        {
            MakeRule("r1", "01", "", "ED15N711.001"),
            MakeRule("r2", "02", "", "ED15N711.001"),
        };

        var conflicts = MappingRuleValidator.DetectConflicts(rules);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void DetectConflicts_MismoCapitulo_MismoCodigo_UnaDeshabilitada_NoEsConflicto()
    {
        var rules = new[]
        {
            MakeRule("r1", "01", "", "ED15N711.001", enabled: true),
            MakeRule("r2", "01", "", "ED15N711.001", enabled: false),
        };

        var conflicts = MappingRuleValidator.DetectConflicts(rules);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void DetectConflicts_MismoCapitulo_MismoCodigo_AmbasHabilitadas_EsConflicto()
    {
        var rules = new[]
        {
            MakeRule("r1", "01", "", "ED15N711.001"),
            MakeRule("r2", "01", "", "ED15N711.001"),
        };

        var conflicts = MappingRuleValidator.DetectConflicts(rules);

        conflicts.Should().HaveCount(1);
        conflicts[0].BC3Code.Should().Be("ED15N711.001");
        conflicts[0].ChapterKey.Should().Be("01");
        conflicts[0].RuleIds.Should().BeEquivalentTo(new[] { "r1", "r2" });
    }

    [Fact]
    public void DetectConflicts_MismoCapituloYSubcapitulo_MismoCodigo_EsConflicto()
    {
        var rules = new[]
        {
            MakeRule("r1", "01", "AFS", "ED15N711.001"),
            MakeRule("r2", "01", "AFS", "ED15N711.001"),
        };

        var conflicts = MappingRuleValidator.DetectConflicts(rules);

        conflicts.Should().HaveCount(1);
        conflicts[0].SubChapterKey.Should().Be("AFS");
        conflicts[0].RuleIds.Should().BeEquivalentTo(new[] { "r1", "r2" });
    }

    [Fact]
    public void DetectConflicts_MismoCapitulo_DistintoSubcapitulo_NoEsConflicto()
    {
        var rules = new[]
        {
            MakeRule("r1", "01", "AFS", "ED15N711.001"),
            MakeRule("r2", "01", "PLU", "ED15N711.001"),
        };

        var conflicts = MappingRuleValidator.DetectConflicts(rules);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void DetectConflicts_BC3CodeVacio_NoCuenta()
    {
        var rules = new[]
        {
            MakeRule("r1", "01", "", ""),
            MakeRule("r2", "01", "", ""),
        };

        var conflicts = MappingRuleValidator.DetectConflicts(rules);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void DetectConflicts_TresReglasDuplicadas_AgrupaTodasEnUnConflicto()
    {
        var rules = new[]
        {
            MakeRule("r1", "01", "", "ED15N711.001"),
            MakeRule("r2", "01", "", "ED15N711.001"),
            MakeRule("r3", "01", "", "ED15N711.001"),
        };

        var conflicts = MappingRuleValidator.DetectConflicts(rules);

        conflicts.Should().HaveCount(1);
        conflicts[0].RuleIds.Should().HaveCount(3)
            .And.BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public void DetectConflicts_NullInput_DevuelveVacio()
    {
        var conflicts = MappingRuleValidator.DetectConflicts(null!);

        conflicts.Should().NotBeNull().And.BeEmpty();
    }

    // ============================================================
    //  FindConflictFor (validacion del candidato al editar)
    // ============================================================

    [Fact]
    public void FindConflictFor_SinChoque_DevuelveNull()
    {
        var existing = new[]
        {
            MakeRule("r1", "01", "", "AAA"),
        };
        var candidate = MakeRule("r2", "02", "", "AAA");

        var conflict = MappingRuleValidator.FindConflictFor(candidate, existing);

        conflict.Should().BeNull();
    }

    [Fact]
    public void FindConflictFor_ChocaConOtraHabilitada_DevuelveConflict()
    {
        var existing = new[]
        {
            MakeRule("r1", "01", "", "ED15N711.001"),
        };
        var candidate = MakeRule("r2", "01", "", "ED15N711.001");

        var conflict = MappingRuleValidator.FindConflictFor(candidate, existing);

        conflict.Should().NotBeNull();
        conflict!.BC3Code.Should().Be("ED15N711.001");
        conflict.RuleIds.Should().Contain("r1").And.Contain("r2");
    }

    [Fact]
    public void FindConflictFor_CandidateInList_NoChocaConsigoMismo()
    {
        // Cuando el dialogo de edicion guarda, suele incluir al propio
        // candidato dentro de la coleccion.  El validador debe ignorarlo
        // (mismo Id) para no marcar falsos positivos.
        var rules = new[]
        {
            MakeRule("r1", "01", "", "ED15N711.001"),
        };
        var candidate = rules[0];

        var conflict = MappingRuleValidator.FindConflictFor(candidate, rules);

        conflict.Should().BeNull();
    }

    [Fact]
    public void FindConflictFor_CandidatoDeshabilitado_NoChoca()
    {
        var existing = new[]
        {
            MakeRule("r1", "01", "", "ED15N711.001"),
        };
        var candidate = MakeRule("r2", "01", "", "ED15N711.001", enabled: false);

        var conflict = MappingRuleValidator.FindConflictFor(candidate, existing);

        conflict.Should().BeNull();
    }

    [Fact]
    public void FindConflictFor_OtraDeshabilitada_NoChoca()
    {
        var existing = new[]
        {
            MakeRule("r1", "01", "", "ED15N711.001", enabled: false),
        };
        var candidate = MakeRule("r2", "01", "", "ED15N711.001");

        var conflict = MappingRuleValidator.FindConflictFor(candidate, existing);

        conflict.Should().BeNull();
    }

    // ============================================================
    //  GetConflictingRuleIds
    // ============================================================

    [Fact]
    public void GetConflictingRuleIds_DevuelveTodosLosIds()
    {
        var rules = new[]
        {
            MakeRule("r1", "01", "", "AAA"),
            MakeRule("r2", "01", "", "AAA"),
            MakeRule("r3", "02", "", "BBB"),
            MakeRule("r4", "02", "", "BBB"),
            MakeRule("r5", "03", "", "ZZZ"), // sola, no choca
        };

        var conflicts = MappingRuleValidator.DetectConflicts(rules);
        var ids = MappingRuleValidator.GetConflictingRuleIds(conflicts);

        ids.Should().BeEquivalentTo(new[] { "r1", "r2", "r3", "r4" });
    }
}
