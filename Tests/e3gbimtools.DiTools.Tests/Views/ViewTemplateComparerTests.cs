using E3G.BimTools.DiTools.Core.Models.Views;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Views;

/// <summary>
/// Tests del modelo <see cref="ViewTemplateDiff"/> y su agregado de diferencias.
/// La logica de comparacion contra Revit la cubre el siguiente sprint con
/// un mock de Document; aqui solo validamos los invariantes del modelo.
/// </summary>
public sealed class ViewTemplateComparerTests
{
    [Fact]
    public void Diff_vacia_significa_plantillas_identicas()
    {
        var diff = new ViewTemplateDiff(
            templateAId: null!, templateAName: "A",
            templateBId: null!, templateBName: "B",
            fieldDiffs: new ViewTemplateFieldDiff[0]);

        diff.AreIdentical.Should().BeTrue();
        diff.FieldDiffs.Should().BeEmpty();
    }

    [Fact]
    public void Diff_con_un_campo_no_es_identica()
    {
        var field = new ViewTemplateFieldDiff(
            section: "General",
            fieldName: "Escala",
            valueA: "100",
            valueB: "50");

        var diff = new ViewTemplateDiff(
            templateAId: null!, templateAName: "A",
            templateBId: null!, templateBName: "B",
            fieldDiffs: new[] { field });

        diff.AreIdentical.Should().BeFalse();
        diff.FieldDiffs.Should().HaveCount(1);
        diff.FieldDiffs[0].Section.Should().Be("General");
    }
}
