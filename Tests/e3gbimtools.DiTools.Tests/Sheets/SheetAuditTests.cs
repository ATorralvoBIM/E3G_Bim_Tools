using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Sheets;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Sheets;

/// <summary>
/// Tests de la lógica de auditoría (huérfanas y duplicados) usando datos
/// de prueba en memoria, sin tocar Revit.
/// La lógica está extraída de <c>SheetGenService.Audit</c> para ser testeable
/// de forma pura.
/// </summary>
public sealed class SheetAuditTests
{
    // Replica la lógica pura de auditoría para tests
    private static SheetAuditResult RunAudit(IList<SheetInfo> sheets)
    {
        var result = new SheetAuditResult();

        foreach (var s in sheets)
            if (s.ViewCount == 0)
                result.OrphanSheets.Add(s);

        var duplicates = sheets
            .GroupBy(s => s.SheetNumber, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var dup in duplicates)
            result.DuplicateNumbers.Add(dup);

        return result;
    }

    [Fact]
    public void Sin_sheets_resultado_limpio()
    {
        var result = RunAudit(new List<SheetInfo>());

        result.IsClean.Should().BeTrue();
        result.OrphanSheets.Should().BeEmpty();
        result.DuplicateNumbers.Should().BeEmpty();
    }

    [Fact]
    public void Detecta_sheets_huerfanas()
    {
        var sheets = new List<SheetInfo>
        {
            new() { SheetNumber = "A-001", SheetName = "Con vistas",     ViewCount = 2 },
            new() { SheetNumber = "A-002", SheetName = "Sin vistas",     ViewCount = 0 },
            new() { SheetNumber = "A-003", SheetName = "También vacía",  ViewCount = 0 },
        };

        var result = RunAudit(sheets);

        result.OrphanSheets.Should().HaveCount(2);
        result.OrphanSheets.Select(s => s.SheetNumber).Should().Contain("A-002").And.Contain("A-003");
        result.DuplicateNumbers.Should().BeEmpty();
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Detecta_numeros_duplicados()
    {
        var sheets = new List<SheetInfo>
        {
            new() { SheetNumber = "A-001", ViewCount = 1 },
            new() { SheetNumber = "A-001", ViewCount = 1 }, // duplicado
            new() { SheetNumber = "A-002", ViewCount = 1 },
        };

        var result = RunAudit(sheets);

        result.DuplicateNumbers.Should().Contain("A-001");
        result.DuplicateNumbers.Should().HaveCount(1);
        result.OrphanSheets.Should().BeEmpty();
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Detecta_huerfanas_y_duplicados_simultaneamente()
    {
        var sheets = new List<SheetInfo>
        {
            new() { SheetNumber = "A-001", ViewCount = 0 }, // huérfana
            new() { SheetNumber = "A-002", ViewCount = 1 },
            new() { SheetNumber = "A-002", ViewCount = 1 }, // duplicado
        };

        var result = RunAudit(sheets);

        result.OrphanSheets.Should().HaveCount(1);
        result.DuplicateNumbers.Should().Contain("A-002");
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Juego_limpio_no_tiene_problemas()
    {
        var sheets = new List<SheetInfo>
        {
            new() { SheetNumber = "A-001", ViewCount = 2 },
            new() { SheetNumber = "A-002", ViewCount = 1 },
            new() { SheetNumber = "A-003", ViewCount = 3 },
        };

        var result = RunAudit(sheets);

        result.IsClean.Should().BeTrue();
        result.ToSummary().Should().Contain("limpio");
    }

    [Fact]
    public void ToSummary_describe_problemas_encontrados()
    {
        var sheets = new List<SheetInfo>
        {
            new() { SheetNumber = "A-001", ViewCount = 0 },
            new() { SheetNumber = "A-002", ViewCount = 1 },
            new() { SheetNumber = "A-002", ViewCount = 1 },
        };

        var result = RunAudit(sheets);
        var summary = result.ToSummary();

        summary.Should().Contain("huérfana");
        summary.Should().Contain("duplicado");
    }
}
