using System;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.UI.RevitBridge;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Coordinador estático del dockable pane "Grafismo de filtros". Mismo ciclo
/// de vida que <see cref="Worksets.WorksetDock"/>: registro en OnStartup,
/// <see cref="Attach"/> en ApplicationInitialized y botón de ribbon para mostrarlo.
/// </summary>
public static class FilterGraphicsDock
{
    /// <summary>Id único del panel (GUID fijo).</summary>
    public static readonly DockablePaneId PaneId =
        new(new Guid("6B1F2E90-4C7A-4D35-A8E2-3F9C1B7D5A42"));

    public const string Title = "Grafismo de filtros";

    private static FilterGraphicsDockControl?   _control;
    private static WorksetDockExternalEventBus? _bus;

    public static FilterGraphicsDockControl GetOrCreateControl()
        => _control ??= new FilterGraphicsDockControl();

    /// <summary>Debe llamarse en contexto de la API (ApplicationInitialized).</summary>
    public static void Attach(UIApplication uiApp)
    {
        if (uiApp is null) return;

        _bus ??= new WorksetDockExternalEventBus();
        GetOrCreateControl().Initialize(uiApp, _bus);

        uiApp.ViewActivated += (_, __) =>
        {
            try { _control?.OnViewActivated(); } catch { /* nunca propagar al evento de Revit */ }
        };
    }

    /// <summary>Refresca desde la vista activa (llamar en contexto API).</summary>
    public static void RequestRefresh()
    {
        try { _control?.OnViewActivated(); } catch { /* ignore */ }
    }

    public static void Shutdown()
    {
        try { _bus?.Dispose(); } catch { }
        _bus = null;
        _control = null;
    }
}

/// <summary>Proveedor del dockable pane "Grafismo de filtros".</summary>
public sealed class FilterGraphicsDockProvider : IDockablePaneProvider
{
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = FilterGraphicsDock.GetOrCreateControl();
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
    }
}
