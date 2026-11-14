using System;
using System.IO;
using System.Linq;
using E3G.BimTools.DiTools.Core.Models.Sheets;
using E3G.BimTools.DiTools.Core.Services.Sheets;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Sheets;

/// <summary>
/// Tests de round-trip JSON del <see cref="SheetLayoutRepository"/>.
/// Usa un directorio temporal para no contaminar %APPDATA%.
/// </summary>
public sealed class SheetLayoutRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SheetLayoutRepository _repo;

    public SheetLayoutRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sheetgen_tests_{Guid.NewGuid():N}");
        _repo = new SheetLayoutRepository(new FakeLoggerFactory(), _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Save_y_GetAll_devuelve_el_mismo_layout()
    {
        var layout = new SheetLayoutDefinition
        {
            Name        = "Layout Tipo A",
            Description = "Descripción de prueba",
            Placements  =
            {
                new ViewPlacementSpec { ViewName = "Planta Baja", XOffsetMm = 100, YOffsetMm = 200, ScaleOverride = 50 },
                new ViewPlacementSpec { ViewName = "Sección 1",   XOffsetMm = 400, YOffsetMm = 200 }
            }
        };

        _repo.Save(layout);
        var all = _repo.GetAll();

        all.Should().HaveCount(1);
        var loaded = all[0];
        loaded.Id.Should().Be(layout.Id);
        loaded.Name.Should().Be("Layout Tipo A");
        loaded.Description.Should().Be("Descripción de prueba");
        loaded.Placements.Should().HaveCount(2);
        loaded.Placements[0].ViewName.Should().Be("Planta Baja");
        loaded.Placements[0].XOffsetMm.Should().Be(100);
        loaded.Placements[0].ScaleOverride.Should().Be(50);
        loaded.Placements[1].ViewName.Should().Be("Sección 1");
        loaded.Placements[1].ScaleOverride.Should().BeNull();
    }

    [Fact]
    public void Delete_elimina_el_layout()
    {
        var layout = new SheetLayoutDefinition { Name = "A borrar" };
        _repo.Save(layout);
        _repo.GetAll().Should().HaveCount(1);

        _repo.Delete(layout.Id);
        _repo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Delete_no_existente_no_lanza_excepcion()
    {
        var act = () => _repo.Delete(Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Fact]
    public void Save_sobreescribe_layout_existente()
    {
        var layout = new SheetLayoutDefinition { Name = "Original" };
        _repo.Save(layout);

        layout.Name = "Modificado";
        _repo.Save(layout);

        var all = _repo.GetAll();
        all.Should().HaveCount(1);
        all[0].Name.Should().Be("Modificado");
    }

    [Fact]
    public void GetAll_directorio_inexistente_devuelve_lista_vacia()
    {
        // _tempDir no existe aún (no hemos guardado nada)
        var freshDir  = Path.Combine(Path.GetTempPath(), $"sheetgen_empty_{Guid.NewGuid():N}");
        var freshRepo = new SheetLayoutRepository(new FakeLoggerFactory(), freshDir);
        try
        {
            freshRepo.GetAll().Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(freshDir)) Directory.Delete(freshDir, true);
        }
    }
}

// ── Doble de test ────────────────────────────────────────────────────────────

file sealed class FakeLoggerFactory : E3G.BimTools.DiTools.Core.Abstractions.ILoggerFactory
{
    public E3G.BimTools.DiTools.Core.Abstractions.ILogger CreateLogger(string category) => new FakeLogger();
    public E3G.BimTools.DiTools.Core.Abstractions.ILogger CreateLogger<T>() => new FakeLogger();
}

file sealed class FakeLogger : E3G.BimTools.DiTools.Core.Abstractions.ILogger
{
    public void Information(string message, params object[] args) { }
    public void Warning(string message, params object[] args)     { }
    public void Error(Exception? ex, string message, params object[] args) { }
    public void Debug(string message, params object[] args)       { }
}
