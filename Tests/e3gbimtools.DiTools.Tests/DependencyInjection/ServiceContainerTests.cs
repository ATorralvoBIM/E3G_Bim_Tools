using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.DependencyInjection;

/// <summary>
/// Verifica que el contenedor de servicios resuelve los servicios nuevos
/// del Sprint 4 (ViewManager). Si alguno falta el test falla con el nombre
/// del servicio no registrado.
/// </summary>
public sealed class ServiceContainerTests
{
    [Fact]
    public void Build_registra_los_servicios_del_Sprint4()
    {
        using var container = DiToolsCoreBuilder.Build();

        container.Resolve<IViewTemplateService>().Should().NotBeNull();
        container.Resolve<IViewRangeService>().Should().NotBeNull();
        container.Resolve<IViewTemplateComparer>().Should().NotBeNull();
        container.Resolve<IViewRangeCompat>().Should().NotBeNull();
    }

    [Fact]
    public void Build_mantiene_los_servicios_de_sprints_anteriores()
    {
        using var container = DiToolsCoreBuilder.Build();

        container.Resolve<ITransactionService>().Should().NotBeNull();
        container.Resolve<IParaManagerService>().Should().NotBeNull();
        container.Resolve<IFilterService>().Should().NotBeNull();
        container.Resolve<ILevelCopyService>().Should().NotBeNull();
    }

    [Fact]
    public void Build_registra_los_servicios_del_Sprint5()
    {
        using var container = DiToolsCoreBuilder.Build();

        container.Resolve<ISectionBoxService>().Should().NotBeNull();
        container.Resolve<IBoundingBoxCalculator>().Should().NotBeNull();
    }

    [Fact]
    public void Build_registra_los_servicios_del_Sprint6()
    {
        using var container = DiToolsCoreBuilder.Build();

        container.Resolve<ISheetGenService>().Should().NotBeNull();
    }
}
