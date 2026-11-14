using System;
using System.Collections.Generic;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Errors;
using E3G.BimTools.DiTools.Core.Logging;
using E3G.BimTools.DiTools.Core.Services;
using E3G.BimTools.DiTools.Core.Services.Filter;
using E3G.BimTools.DiTools.Core.Services.Levels;
using E3G.BimTools.DiTools.Core.Services.Parameters;
using E3G.BimTools.DiTools.Core.Services.Families;
using E3G.BimTools.DiTools.Core.Services.Sections;
using E3G.BimTools.DiTools.Core.Services.Sections.Sources;
using E3G.BimTools.DiTools.Core.Services.Sheets;
using E3G.BimTools.DiTools.Core.Services.Views;
using E3G.BimTools.DiTools.Core.Services.Views.Compat;
using E3G.BimTools.DiTools.Core.Services.ViewSheets;
using E3G.BimTools.DiTools.Core.Services.TableGen;
using E3G.BimTools.DiTools.Core.Services.DxfLegend;
using E3G.BimTools.DiTools.Core.Services.DialuxImport;
using E3G.BimTools.DiTools.Core.Services.RefrigerantRouting;
using E3G.BimTools.DiTools.Core.Services.Worksets;
using E3G.BimTools.DiTools.Core.Services.OneParameter;
using E3G.BimTools.DiTools.Core.Services.SheetLink;
using E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.DependencyInjection;

/// <summary>
/// Contenedor minimo de servicios (Service Locator simple) para el Sprint 0.
/// Sustituye a Microsoft.Extensions.DependencyInjection mientras los paquetes
/// NuGet no funcionan.  Cuando NuGet vuelva a funcionar (Sprint 1+) se migra a DI.
/// </summary>
public sealed class DiToolsServiceContainer : IDisposable
{
    private readonly Dictionary<Type, object> _singletons = new();
    private bool _disposed;

    public void Register<TService>(TService instance) where TService : notnull
    {
        _singletons[typeof(TService)] = instance;
    }

    public TService Resolve<TService>() where TService : notnull
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DiToolsServiceContainer));
        if (_singletons.TryGetValue(typeof(TService), out var instance))
            return (TService)instance;
        throw new InvalidOperationException(
            $"Servicio no registrado: {typeof(TService).FullName}. " +
            "Revisa que DiToolsCoreBuilder.Build(...) lo registre.");
    }

    public bool TryResolve<TService>(out TService? instance) where TService : class
    {
        if (_singletons.TryGetValue(typeof(TService), out var obj))
        {
            instance = (TService)obj;
            return true;
        }
        instance = null;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var s in _singletons.Values)
            if (s is IDisposable d) try { d.Dispose(); } catch { }
        _singletons.Clear();
    }
}

/// <summary>
/// Crea el contenedor con todos los servicios de .Core registrados.
/// </summary>
public static class DiToolsCoreBuilder
{
    public static DiToolsServiceContainer Build(string? revitVersion = null)
    {
        var container = new DiToolsServiceContainer();

        var loggerFactory = new SerilogLoggerFactory(revitVersion);
        container.Register<ILoggerFactory>(loggerFactory);
        container.Register<GlobalExceptionHandler>(new GlobalExceptionHandler(loggerFactory));
        container.Register<IParameterService>(new ParameterService());
        var txService = new TransactionService(loggerFactory);
        container.Register<ITransactionService>(txService);
        container.Register<ILevelCopyService>(new LevelCopyService(txService, loggerFactory));

        // OneFilter (Sprint 2)
        container.Register<IFilterService>(new FilterService(loggerFactory));
        container.Register<IFilterTemplateService>(new FilterTemplateService(loggerFactory));
        container.Register<IFilterBatchEditService>(new FilterBatchEditService(txService, loggerFactory));

        // ParaManager (Sprint 3)
        container.Register<IParaManagerService>(new ParaManagerService(txService, loggerFactory));
        container.Register<IParameterValueBulkEditService>(new ParameterValueBulkEditService(txService, loggerFactory));

        // ViewManager (Sprint 4)
        // IExcelService es esqueleto compartido; lo registramos una sola vez aqui
        // porque ningun sprint anterior lo necesitaba.
        var excelService = new ExcelService();
        container.Register<IExcelService>(excelService);

        var viewRangeCompat = new ViewRangeCompat_Default();
        container.Register<IViewRangeCompat>(viewRangeCompat);
        container.Register<IViewRangeService>(new ViewRangeService(txService, viewRangeCompat, loggerFactory));
        container.Register<IViewTemplateService>(new ViewTemplateService(txService, excelService, loggerFactory));
        container.Register<IViewTemplateComparer>(new ViewTemplateComparer(loggerFactory));

        // SectionBoxer (Sprint 5)
        var bboxCalc = new BoundingBoxCalculator(loggerFactory);
        container.Register<IBoundingBoxCalculator>(bboxCalc);
        var sectionSources = new ISectionBoxSource[]
        {
            new SelectionSource(),
            new RoomSource(),
            new LevelSource(),
            new SavedFilterSource(
                container.Resolve<IFilterTemplateService>(),
                container.Resolve<IFilterService>()),
        };
        container.Register<ISectionBoxService>(
            new SectionBoxService(txService, bboxCalc, sectionSources, loggerFactory));

        // SheetGen (Sprint 6)
        var sheetLayoutRepo = new SheetLayoutRepository(loggerFactory);
        container.Register<ISheetGenService>(
            new SheetGenService(txService, excelService, sheetLayoutRepo, loggerFactory));

        // View & Sheet Manager (planos MEP — doble bucle instalaciones × niveles)
        container.Register<InstalacionConfigRepository>(new InstalacionConfigRepository(loggerFactory));
        container.Register<IViewSheetGenerator>(new ViewSheetGenerator(txService, loggerFactory));

        // FamilyReviser (Sprint 7)
        container.Register<IFamilyReviserService>(new FamilyReviserService(txService, loggerFactory));
        container.Register<IWorksetService>(new WorksetService(txService, loggerFactory));

        // TableGen (Sprint 8)
        container.Register<ITableGenService>(new TableGenService(loggerFactory));

        // Leyenda desde DXF (AutoCAD → leyenda/vista de dibujo con las mismas medidas)
        container.Register<IDxfLegendService>(new DxfLegendService(loggerFactory));

        // DIALux → Revit (DWG de luminarias → familias del modelo; sustituye a DIALux_a_Revit.dyn)
        container.Register<IDialuxImportService>(new DialuxImportService(loggerFactory));

        // Climatizacion: trazado de refrigerante desde DXF (LG LATS → tuberias 50_12 / 50_13 y accesorios)
        container.Register<IRefrigerantRoutingService>(new RefrigerantRoutingService(loggerFactory));

        // OneParameter (Sprint 10)
        container.Register<IOneParameterService>(new OneParameterService(txService, loggerFactory));

        // SheetLink (Sprint 11)
        container.Register<ISheetLinkService>(new SheetLinkService(txService, loggerFactory));

        // Tablas de planificacion ↔ Excel (por ahora solo exportar con formato)
        container.Register<IScheduleExcelService>(new ScheduleExcelService(loggerFactory));

        // ISelectionService y IRevitContext los registra el proyecto host porque
        // dependen de UIApplication.

        return container;
    }
}
