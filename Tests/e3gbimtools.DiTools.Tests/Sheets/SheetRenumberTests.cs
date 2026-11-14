using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Sheets;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Sheets;

/// <summary>
/// Tests del patrón de renumeración de <see cref="SheetRenumberRequest"/>
/// sin tocar Revit.
/// </summary>
public sealed class SheetRenumberTests
{
    [Fact]
    public void ComputeNumber_solo_secuencia()
    {
        var req = new SheetRenumberRequest { Prefix = "", Suffix = "", StartIndex = 1, PadDigits = 2 };

        req.ComputeNumber(0).Should().Be("01");
        req.ComputeNumber(1).Should().Be("02");
        req.ComputeNumber(9).Should().Be("10");
    }

    [Fact]
    public void ComputeNumber_con_prefijo()
    {
        var req = new SheetRenumberRequest { Prefix = "A-", Suffix = "", StartIndex = 1, PadDigits = 3 };

        req.ComputeNumber(0).Should().Be("A-001");
        req.ComputeNumber(4).Should().Be("A-005");
    }

    [Fact]
    public void ComputeNumber_con_prefijo_y_sufijo()
    {
        var req = new SheetRenumberRequest { Prefix = "E", Suffix = "-REV", StartIndex = 10, PadDigits = 2 };

        req.ComputeNumber(0).Should().Be("E10-REV");
        req.ComputeNumber(1).Should().Be("E11-REV");
    }

    [Fact]
    public void StartIndex_cero()
    {
        var req = new SheetRenumberRequest { Prefix = "", Suffix = "", StartIndex = 0, PadDigits = 2 };

        req.ComputeNumber(0).Should().Be("00");
        req.ComputeNumber(1).Should().Be("01");
    }

    [Fact]
    public void Lista_vacia_es_invalida()
    {
        var req = new SheetRenumberRequest();

        req.IsValid(out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PadDigits_menor_que_uno_es_invalido()
    {
        var req = new SheetRenumberRequest
        {
            SheetIds  = new[] { new ElementId(1) },
            PadDigits = 0
        };

        req.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("PadDigits");
    }

    [Fact]
    public void StartIndex_negativo_es_invalido()
    {
        var req = new SheetRenumberRequest
        {
            SheetIds   = new[] { new ElementId(1) },
            StartIndex = -1,
            PadDigits  = 2
        };

        req.IsValid(out var error).Should().BeFalse();
        error.Should().Contain("negativo");
    }

    [Fact]
    public void Request_valido_pasa()
    {
        var req = new SheetRenumberRequest
        {
            SheetIds   = new[] { new ElementId(1), new ElementId(2) },
            Prefix     = "A-",
            StartIndex = 1,
            PadDigits  = 2
        };

        req.IsValid(out var error).Should().BeTrue();
        error.Should().BeNull();
    }
}
