using System;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.RevitBridge;

namespace E3G.BimTools.DiTools.UI.Views.Worksets;

/// <summary>
/// Coordinador estático del dockable pane de visibilidad de worksets (Bloque E).
///
/// Ciclo de vida:
///   1. <see cref="PaneId"/> se registra en DiToolsApplication.OnStartup
///      con <see cref="WorksetVisibilityDockProvider"/>.
///   2. <see cref="Attach"/> se llama en ApplicationInitialized (contexto API
///      válido) para inyectar UIApplication + servicio, crear el bus de
///      ExternalEvent y suscribirse a ViewActivated.
///   3. El usuario muestra el panel con un botón; Revit pide el control vía
///      <see cref="GetOrCreateControl"/>.
/// </summary>
public static class WorksetDock
{
    /// <summary>Id único del panel (GUID fijo).</summary>
    public static readonly DockablePaneId PaneId =
        new(new Guid("E3D17C40-2B6A-4E83-9F2C-7A1B6E5D4C30"));

    public const string Title = "Visibilidad de worksets";

    private static WorksetVisibilityDockControl? _control;
    private static WorksetDockExternalEventBus?   _bus;

    /// <summary>Crea (una vez) y devuelve el control WPF del panel.</summary>
    public static WorksetVisibilityDockControl GetOrCreateControl()
        => _control ??= new WorksetVisibilityDockControl();

    /// <summary>
    /// Inyecta dependencias y engancha ViewActivated. Debe llamarse en contexto
    /// de la API de Revit (p. ej. ApplicationInitialized).
    /// </summary>
    public static void Attach(UIApplication uiApp, IWorksetService service)
    {
        if (uiApp is null || service is null) return;

        _bus ??= new WorksetDockExternalEventBus();
        var control = GetOrCreateControl();
        control.Initialize(uiApp, service, _bus);

        uiApp.ViewActivated += (_, __) =>
        {
            try { _control?.OnViewActivated(); } catch { /* nunca propagar al evento de Revit */ }
        };
    }

    /// <summary>
    /// Fuerza un refresco del panel desde la vista activa actual. Seguro de
    /// llamar desde un comando (contexto API válido).
    /// </summary>
    public static void RequestRefresh()
    {
        try { _control?.OnViewActivated(); } catch { /* ignore */ }
    }

    /// <summary>Libera el bus de ExternalEvent (OnShutdown).</summary>
    public static void Shutdown()
    {
        try { _bus?.Dispose(); } catch { }
        _bus = null;
        _control = null;
    }
}
