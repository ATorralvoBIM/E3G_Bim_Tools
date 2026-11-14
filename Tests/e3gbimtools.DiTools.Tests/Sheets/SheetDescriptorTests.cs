using E3G.BimTools.DiTools.Core.Models.Sheets;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Sheets;

/// <summary>
/// Tests de validación básica de <see cref="SheetDescriptor"/> sin tocar Revit.
/// </summary>
public sealed class SheetDescriptorTests
{
    [Fact]
    public void SheetNumber_vacio_es_invalido()
    {
        var d = new SheetDescriptor
        {
            SheetNumber             = "  ",
            SheetName               = "Planta baja",
            TitleBlockFamilyTypeName = "A1 metric : A1 metric"
        };

        d.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("SheetNumber");
    }

    [Fact]
    public void SheetName_vacio_es_invalido()
    {
        var d = new SheetDescriptor
        {
            SheetNumber             = "A-001",
            SheetName               = "",
            TitleBlockFamilyTypeName = "A1 metric : A1 metric"
        };

        d.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("SheetName");
    }

    [Fact]
    public void TitleBlock_vacio_es_invalido()
    {
        var d = new SheetDescriptor
        {
            SheetNumber             = "A-001",
            SheetName               = "Planta baja",
            TitleBlockFamilyTypeName = "   "
        };

        d.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("TitleBlock");
    }

    [Fact]
    public void Descriptor_completo_es_valido()
    {
        var d = new SheetDescriptor
        {
            SheetNumber             = "A-001",
            SheetName               = "Planta baja",
            TitleBlockFamilyTypeName = "A1 metric : A1 metric",
            Discipline              = "Architecture"
        };

        d.IsValid(out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void Discipline_es_opcional()
    {
        var d = new SheetDescriptor
        {
            SheetNumber             = "S-010",
            SheetName               = "Sección transversal",
            TitleBlockFamilyTypeName = "A3 : A3"
        };

        d.IsValid(out var error).Should().BeTrue();
        error.Should().BeNull();
        d.Discipline.Should().BeNull();
    }
}
