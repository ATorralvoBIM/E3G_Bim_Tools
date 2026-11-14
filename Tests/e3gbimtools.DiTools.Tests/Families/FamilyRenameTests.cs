using E3G.BimTools.DiTools.Core.Models.Families;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Families;

/// <summary>
/// Tests del motor de renombrado de <see cref="FamilyRenameRequest"/>
/// sin tocar Revit (lógica pura).
/// </summary>
public sealed class FamilyRenameTests
{
    // ================================================================== //
    //  ComputeProposedName — transformaciones básicas                     //
    // ================================================================== //

    [Fact]
    public void Prefijo_se_antepone_al_nombre()
    {
        var req = new FamilyRenameRequest { Prefix = "[ARQ] " };
        req.ComputeProposedName("Puerta_Simple").Should().Be("[ARQ] Puerta_Simple");
    }

    [Fact]
    public void Sufijo_se_pospone_al_nombre()
    {
        var req = new FamilyRenameRequest { Suffix = "_v2" };
        req.ComputeProposedName("Ventana_Corredera").Should().Be("Ventana_Corredera_v2");
    }

    [Fact]
    public void Prefijo_y_sufijo_combinados()
    {
        var req = new FamilyRenameRequest { Prefix = "E3G_", Suffix = "_2025" };
        req.ComputeProposedName("Columna").Should().Be("E3G_Columna_2025");
    }

    [Fact]
    public void FindReplace_sustituye_ocurrencia()
    {
        var req = new FamilyRenameRequest { FindText = "PLANTA", ReplaceText = "NIVEL" };
        req.ComputeProposedName("PLANTA_BAJA").Should().Be("NIVEL_BAJA");
    }

    [Fact]
    public void FindReplace_sustituye_todas_las_ocurrencias()
    {
        var req = new FamilyRenameRequest { FindText = "XX", ReplaceText = "00" };
        req.ComputeProposedName("XX_Muro_XX").Should().Be("00_Muro_00");
    }

    [Fact]
    public void FindReplace_insensible_a_mayusculas_por_defecto()
    {
        var req = new FamilyRenameRequest { FindText = "puerta", ReplaceText = "Puerta", CaseSensitive = false };
        req.ComputeProposedName("PUERTA_doble").Should().Be("Puerta_doble");
    }

    [Fact]
    public void FindReplace_sensible_a_mayusculas_cuando_se_activa()
    {
        var req = new FamilyRenameRequest { FindText = "puerta", ReplaceText = "PUERTA", CaseSensitive = true };
        // "PUERTA_doble" no tiene "puerta" (minúsculas) → no cambia
        req.ComputeProposedName("PUERTA_doble").Should().Be("PUERTA_doble");
    }

    [Fact]
    public void FindText_vacio_no_modifica_el_nombre()
    {
        var req = new FamilyRenameRequest { FindText = "", ReplaceText = "X" };
        req.ComputeProposedName("Familia").Should().Be("Familia");
    }

    [Fact]
    public void Todo_a_la_vez_aplica_en_orden_find_prefijo_sufijo()
    {
        // El orden es: find/replace → prefijo → sufijo
        var req = new FamilyRenameRequest
        {
            FindText    = "Old",
            ReplaceText = "New",
            Prefix      = "[",
            Suffix      = "]",
        };
        req.ComputeProposedName("Old_Family").Should().Be("[New_Family]");
    }

    [Fact]
    public void Nombre_sin_cambios_si_no_hay_reglas()
    {
        var req = new FamilyRenameRequest();
        req.ComputeProposedName("Mi Familia").Should().Be("Mi Familia");
    }

    // ================================================================== //
    //  HasRenameChange                                                     //
    // ================================================================== //

    [Fact]
    public void HasRenameChange_true_cuando_proposed_difiere()
    {
        var fd = new FamilyDescriptor { Name = "Puerta", ProposedName = "[ARQ] Puerta" };
        fd.HasRenameChange.Should().BeTrue();
    }

    [Fact]
    public void HasRenameChange_false_cuando_proposed_igual()
    {
        var fd = new FamilyDescriptor { Name = "Puerta", ProposedName = "Puerta" };
        fd.HasRenameChange.Should().BeFalse();
    }

    [Fact]
    public void HasRenameChange_false_cuando_proposed_vacio()
    {
        var fd = new FamilyDescriptor { Name = "Puerta", ProposedName = string.Empty };
        fd.HasRenameChange.Should().BeFalse();
    }
}
