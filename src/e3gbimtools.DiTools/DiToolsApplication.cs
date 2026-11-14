using System;
using System.Windows;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Errors;
using E3G.BimTools.DiTools.Ribbon;
using E3G.BimTools.DiTools.UI.Dialogs;
using E3G.BimTools.DiTools.UI.Views.FilterGraphics;
using E3G.BimTools.DiTools.UI.Views.Worksets;

namespace E3G.BimTools.DiTools;

/// <summary>
/// Punto de entrada del add-in.  Revit invoca <see cref="OnStartup"/> una vez
/// al cargar la sesion y <see cref="OnShutdown"/> al cerrarla.
///
/// Sprint 0: construye los servicios compartidos, engancha el manejador
/// global de excepciones y crea los paneles "Inicio Proyecto" y "DiTools"
/// con un boton About cada uno.
/// </summary>
public sealed class DiToolsApplication : IExternalApplication
{
    private ILogger? _logger;
    private GlobalExceptionHandler? _errorHandler;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            var revitVersion = application.ControlledApplication.VersionNumber;

            // 1) Inicializa servicios (.Core) + el RevitContext.
            ServiceContainer.Initialize(application, revitVersion);

            // 2) Loggea arranque.
            _logger = ServiceContainer.Resolve<ILoggerFactory>().CreateLogger<DiToolsApplication>();
            _logger.Information(
                "DiTools arrancando.  Producto={0} Version={1} Revit={2}",
                E3G.BimTools.DiTools.Core.ProjectInfo.ProductName,
                E3G.BimTools.DiTools.Core.ProjectInfo.AssemblyVersion,
                revitVersion);

            // 3) Engancha el manejador global de excepciones.
            _errorHandler = ServiceContainer.Resolve<GlobalExceptionHandler>();
            _errorHandler.ShowDialog = ex =>
            {
                try
                {
                    var dlg = FriendlyErrorDialog.ForException(ex);
                    dlg.ShowDialog();
                }
                catch { /* nunca propagar desde el handler */ }
            };
            _errorHandler.Attach();

            // Dispatcher de WPF tambien debe redirigir al handler.
            if (Application.Current is not null)
            {
                Application.Current.DispatcherUnhandledException += (s, e) =>
                {
                    _errorHandler.Handle(e.Exception);
                    e.Handled = true;
                };
            }

            // 3b) Modulo Saneamiento (e3gbimtools.Saneamiento.dll): registra sus
            //     paneles acoplables y eventos.  Debe ir antes de la cinta.
            if (SaneamientoBridge.TryInitialize(application))
            {
                _logger.Information("Modulo Saneamiento cargado.");

                // DIALux → Revit es una pagina del panel MEP (2026-11-11): habla con
                // Revit por ExternalEvent, que hay que crear aqui (contexto de API).
                // Si el panel no la acepta, la cinta conserva su boton (ventana modal).
                var invocador = new E3G.BimTools.DiTools.UI.Hosting.ExternalEventRevitInvoker(
                    ServiceContainer.AttachUiApplication);
                RibbonBuilder.DialuxEnPanelMep = SaneamientoBridge.RegistrarPagina("dialux", () =>
                    new E3G.BimTools.DiTools.UI.Views.DialuxImport.DialuxImportView(
                        ServiceContainer.Resolve<IDialuxImportService>(), invocador, integrada: true));

                // Climatizacion (2026-11-12): esquema de principio desde DXF, con el
                // lector/generador de «Leyenda desde DXF».
                // Va en una pestaña de la pagina Climatizacion, que ya pone la cabecera.
                SaneamientoBridge.RegistrarPagina("esquema-dxf", () =>
                    new E3G.BimTools.DiTools.UI.Views.SchematicDxf.SchematicDxfView(
                        ServiceContainer.Resolve<IDxfLegendService>(), invocador, integrada: false));

                // Climatizacion (2026-11-12): trazado de refrigerante desde el DXF del
                // fabricante (LG LATS) y sus ajustes, como pestañas de la pagina.
                SaneamientoBridge.RegistrarPagina("clima-trazado", () =>
                    new E3G.BimTools.DiTools.UI.Views.RefrigerantRouting.RefrigerantRoutingView(
                        ServiceContainer.Resolve<IRefrigerantRoutingService>(), invocador));
                SaneamientoBridge.RegistrarPagina("clima-ajustes", () =>
                    new E3G.BimTools.DiTools.UI.Views.RefrigerantRouting.RefrigerantSettingsView(
                        ServiceContainer.Resolve<IRefrigerantRoutingService>(), invocador));
            }
            else
                _logger.Warning("Modulo Saneamiento no disponible: {0}", SaneamientoBridge.LoadError ?? "?");

            // 4) Construye el ribbon (MEP primero, luego Inicio Proyecto, DiTools...).
            RibbonBuilder.Build(application);

            // 4b) Registra el dockable pane de visibilidad de worksets (Bloque E).
            //     El control y las dependencias se acoplan en ApplicationInitialized.
            try
            {
                application.RegisterDockablePane(
                    WorksetDock.PaneId, WorksetDock.Title, new WorksetVisibilityDockProvider());
            }
            catch (Exception ex)
            {
                _logger?.Warning("No se pudo registrar el dockable pane de worksets: {0}", ex.Message);
            }

            // 4c) Registra el dockable pane "Grafismo de filtros".
            try
            {
                application.RegisterDockablePane(
                    FilterGraphicsDock.PaneId, FilterGraphicsDock.Title, new FilterGraphicsDockProvider());
            }
            catch (Exception ex)
            {
                _logger?.Warning("No se pudo registrar el dockable pane de filtros: {0}", ex.Message);
            }

            // 5) Engancha ApplicationInitialized para completar el RevitContext
            //    en cuanto este disponible la UIApplication real.
            application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;

            _logger.Information("DiTools arrancado correctamente.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            // Sin logger aun?  Al menos un MessageBox.
            try { _logger?.Error(ex, "Fallo en OnStartup."); } catch { }
            MessageBox.Show(
                "No se pudo iniciar el modulo DiTools.\r\n\r\n" + ex.Message,
                "e3gbimtools",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            _logger?.Information("DiTools cerrando.");
            WorksetDock.Shutdown();
            FilterGraphicsDock.Shutdown();
            _errorHandler?.Detach();
            ServiceContainer.Shutdown();
            return Result.Succeeded;
        }
        catch
        {
            return Result.Failed;
        }
    }

    private void OnApplicationInitialized(object? sender, ApplicationInitializedEventArgs e)
    {
        try
        {
            if (sender is Autodesk.Revit.ApplicationServices.Application app)
            {
                // En ApplicationInitialized podemos construir UIApplication a partir de la Application.
                var uiApp = new UIApplication(app);
                ServiceContainer.AttachUiApplication(uiApp);
                _logger?.Information("UIApplication acoplada al contexto.");

                // Acopla el dockable pane de worksets ahora que hay UIApplication
                // y estamos en contexto válido de la API (para crear el ExternalEvent).
                try
                {
                    var worksetService = ServiceContainer.Resolve<IWorksetService>();
                    WorksetDock.Attach(uiApp, worksetService);
                    _logger?.Information("Dockable pane de visibilidad de worksets acoplado.");
                }
                catch (Exception exDock)
                {
                    _logger?.Warning("No se pudo acoplar el dockable pane de worksets: {0}", exDock.Message);
                }

                try
                {
                    FilterGraphicsDock.Attach(uiApp);
                    _logger?.Information("Dockable pane de grafismo de filtros acoplado.");
                }
                catch (Exception exDock)
                {
                    _logger?.Warning("No se pudo acoplar el dockable pane de filtros: {0}", exDock.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Fallo acoplando UIApplication al contexto.");
        }
    }
}
