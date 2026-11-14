using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Bootstrap;

/// <summary>
/// Implementacion del <see cref="IRevitContext"/>.  Se inicializa con la
/// <see cref="ControlledApplication"/> en <c>DiToolsApplication.OnStartup</c>
/// y se completa con la <see cref="UIApplication"/> cuando Revit termina de
/// cargar (evento <c>ApplicationInitialized</c>).
/// </summary>
internal sealed class RevitContext : IRevitContext
{
    private UIApplication? _uiApp;

    public ControlledApplication ControlledApp { get; }

    public RevitContext(ControlledApplication controlledApplication)
    {
        ControlledApp = controlledApplication ?? throw new ArgumentNullException(nameof(controlledApplication));
    }

    /// <summary>Asigna la UIApplication una vez Revit este completamente cargado.</summary>
    public void AttachUiApplication(UIApplication uiApp)
    {
        _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
    }

    public UIApplication UiApp =>
        _uiApp ?? throw new InvalidOperationException(
            "UIApplication aun no esta lista (Revit no ha terminado de iniciar).");

    public UIDocument? ActiveUiDocument => _uiApp?.ActiveUIDocument;

    public Document? ActiveDocument => _uiApp?.ActiveUIDocument?.Document;

    public bool HasActiveDocument => ActiveDocument is not null;
}
