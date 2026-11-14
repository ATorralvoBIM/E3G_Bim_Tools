using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Sections;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Sections;

/// <summary>
/// Tests sobre la validacion del <see cref="SectionBoxRequest"/> sin tocar
/// Revit.  Solo verifica las reglas de campos obligatorios y padding.
/// </summary>
public sealed class SectionBoxRequestTests
{
    [Fact]
    public void Padding_negativo_es_invalido()
    {
        var req = new SectionBoxRequest(
            kind: SectionBoxSourceKind.Selection,
            paddingMeters: -0.1,
            includeLinks: false,
            alignment: SectionBoxAlignment.Project,
            createDedicatedView: false,
            selectedElementIds: new[] { new ElementId(1) });

        req.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("padding");
    }

    [Fact]
    public void Selection_sin_elementos_es_invalido()
    {
        var req = new SectionBoxRequest(
            kind: SectionBoxSourceKind.Selection,
            paddingMeters: 0.5,
            includeLinks: false,
            alignment: SectionBoxAlignment.Project,
            createDedicatedView: false,
            selectedElementIds: null);

        req.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("seleccionados");
    }

    [Fact]
    public void Room_sin_RoomId_es_invalido()
    {
        var req = new SectionBoxRequest(
            kind: SectionBoxSourceKind.Room,
            paddingMeters: 0.5,
            includeLinks: false,
            alignment: SectionBoxAlignment.Project,
            createDedicatedView: false);

        req.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("habitacion");
    }

    [Fact]
    public void CreateDedicatedView_sin_nombre_es_invalido()
    {
        var req = new SectionBoxRequest(
            kind: SectionBoxSourceKind.Selection,
            paddingMeters: 0.5,
            includeLinks: false,
            alignment: SectionBoxAlignment.Project,
            createDedicatedView: true,
            dedicatedViewName: "  ",
            selectedElementIds: new[] { new ElementId(1) });

        req.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("nombre");
    }

    [Fact]
    public void Request_completo_es_valido()
    {
        var req = new SectionBoxRequest(
            kind: SectionBoxSourceKind.Selection,
            paddingMeters: 0.5,
            includeLinks: true,
            alignment: SectionBoxAlignment.View,
            createDedicatedView: true,
            dedicatedViewName: "3D - Cuarto tecnico",
            selectedElementIds: new[] { new ElementId(1), new ElementId(2) });

        req.IsValid(out var error).Should().BeTrue();
        error.Should().BeNull();
    }
}
