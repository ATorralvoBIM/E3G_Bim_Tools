using System;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.DependencyInjection;
using E3G.BimTools.DiTools.Core.Services;
using e3gbimtools.Calc.DependencyInjection;

namespace E3G.BimTools.DiTools.Bootstrap;

/// <summary>
/// Contenedor global de servicios del add-in.  Sustituye al ServiceProvider
/// de Microsoft.Extensions.DependencyInjection mientras los paquetes NuGet no
/// estan disponibles (Sprint 0).
/// </summary>
internal static class ServiceContainer
{
    private static DiToolsServiceContainer? _container;
    private static RevitContext? _revitContext;

    public static DiToolsServiceContainer Current =>
        _container ?? throw new InvalidOperationException(
            "ServiceContainer no inicializado.  DiToolsApplication.OnStartup debe ejecutarse antes.");

    public static RevitContext RevitContext =>
        _revitContext ?? throw new InvalidOperationException("RevitContext no inicializado.");

    public static void Initialize(UIControlledApplication uiCtrlApp, string revitVersion)
    {
        if (_container is not null) return;

        _revitContext = new RevitContext(uiCtrlApp.ControlledApplication);

        _container = DiToolsCoreBuilder.Build(revitVersion);

        // Servicios del motor MEP (e3gbimtools.Calc). Se registran aqui y no
        // dentro de DiToolsCoreBuilder para evitar una referencia circular
        // entre DiTools.Core y Calc (Calc ya depende de DiTools.Core).
        _container.AddCalcServices();

        _container.Register<IRevitContext>(_revitContext);
        _container.Register<ISelectionService>(new SelectionService(_revitContext));
        _container.Register<IRevitUiDispatcher>(new RevitUiDispatcher());
    }

    public static void AttachUiApplication(UIApplication uiApp) =>
        _revitContext?.AttachUiApplication(uiApp);

    public static void Shutdown()
    {
        _container?.Dispose();
        _container = null;
        _revitContext = null;
    }

    public static T Resolve<T>() where T : notnull => Current.Resolve<T>();
}
