using E3G.BimTools.DiTools.Core.Models.Families;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Families;

/// <summary>
/// Tests de la resolución de rutas de <see cref="FamilyExportRule"/>
/// sin tocar Revit (lógica pura).
/// </summary>
public sealed class FamilyExportRuleTests
{
    private static FamilyDescriptor MakeFamily(
        string name, string category = "Doors", string discipline = "Arquitectura") =>
        new()
        {
            Name         = name,
            ProposedName = name,
            CategoryName = category,
            Discipline   = discipline,
        };

    [Fact]
    public void Plantilla_por_defecto_genera_ruta_correcta()
    {
        var rule   = new FamilyExportRule(); // {Categoria}\{Familia}.rfa
        var family = MakeFamily("Puerta Simple", "Doors");

        var result = rule.Resolve(family);

        result.Should().Be(@"Doors\Puerta Simple.rfa");
    }

    [Fact]
    public void Plantilla_con_disciplina()
    {
        var rule   = new FamilyExportRule { PathTemplate = @"{Disciplina}\{Categoria}\{Familia}.rfa" };
        var family = MakeFamily("Ventana Corredera", "Windows", "Arquitectura");

        var result = rule.Resolve(family);

        result.Should().Be(@"Arquitectura\Windows\Ventana Corredera.rfa");
    }

    [Fact]
    public void Caracteres_invalidos_en_nombre_se_sanitizan()
    {
        var rule   = new FamilyExportRule { PathTemplate = @"{Categoria}\{Familia}.rfa" };
        var family = MakeFamily("Puerta: Doble/Triple", "Doors");

        var result = rule.Resolve(family);

        result.Should().NotContain(":");
        result.Should().NotContain("/");
        result.Should().Contain("Doors");
        result.Should().EndWith(".rfa");
    }

    [Fact]
    public void ProposedName_se_usa_cuando_existe()
    {
        var rule   = new FamilyExportRule();
        var family = MakeFamily("Nombre Viejo", "Furniture");
        family.ProposedName = "Nombre Nuevo";

        var result = rule.Resolve(family);

        result.Should().Contain("Nombre Nuevo");
        result.Should().NotContain("Nombre Viejo");
    }

    [Fact]
    public void Extension_rfa_se_añade_si_falta_en_plantilla()
    {
        var rule   = new FamilyExportRule { PathTemplate = @"{Categoria}\{Familia}" }; // sin .rfa
        var family = MakeFamily("Columna");

        var result = rule.Resolve(family);

        result.Should().EndWith(".rfa");
    }

    [Fact]
    public void Categoria_nula_se_reemplaza_por_guion_bajo()
    {
        var rule   = new FamilyExportRule();
        var family = new FamilyDescriptor
        {
            Name         = "Sin Categoria",
            ProposedName = "Sin Categoria",
            CategoryName = string.Empty,  // vacío
            Discipline   = "Genérico",
        };

        var result = rule.Resolve(family);
        result.Should().StartWith("_");
    }

    [Fact]
    public void ExportRequest_invalido_sin_carpeta()
    {
        var req = new FamilyExportRequest
        {
            Families   = new[] { MakeFamily("F") },
            RootFolder = string.Empty,
        };

        req.IsValid(out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ExportRequest_invalido_sin_familias()
    {
        var req = new FamilyExportRequest
        {
            RootFolder = @"C:\temp",
        };

        req.IsValid(out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ExportRequest_valido_pasa()
    {
        var req = new FamilyExportRequest
        {
            Families   = new[] { MakeFamily("F") },
            RootFolder = @"C:\temp",
        };

        req.IsValid(out var error).Should().BeTrue();
        error.Should().BeNull();
    }
}
