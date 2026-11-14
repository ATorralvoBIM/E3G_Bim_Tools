using Autodesk.Revit.UI;

namespace E3G.BimTools.DiTools.UI.Views.Worksets;

/// <summary>
/// Proveedor del dockable pane de visibilidad de worksets (Bloque E).
/// Revit invoca <see cref="SetupDockablePane"/> de forma diferida la primera
/// vez que se muestra el panel; para entonces ya existe el control creado por
/// <see cref="WorksetDock"/>.
/// </summary>
public sealed class WorksetVisibilityDockProvider : IDockablePaneProvider
{
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = WorksetDock.GetOrCreateControl();
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right,
        };
    }
}
