// ============================================================================
//  CalcServiceContainerExtensions.cs
//  Extension de DiToolsServiceContainer para registrar los servicios del
//  motor de calculo MEP (e3gbimtools.Calc).
//
//  Por que vive aqui (en el proyecto Calc) y no dentro de DiToolsCoreBuilder:
//  e3gbimtools.Calc ya referencia a DiTools.Core; si DiTools.Core conociese a
//  Calc tendriamos una referencia circular.  La solucion estandar es exponer
//  un metodo de extension AddCalcServices y llamarlo desde el host del
//  add-in (Bootstrap/ServiceContainer en e3gbimtools.DiTools), justo despues
//  de DiToolsCoreBuilder.Build().
// ============================================================================

using System;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.DependencyInjection;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Services;

namespace e3gbimtools.Calc.DependencyInjection
{
    public static class CalcServiceContainerExtensions
    {
        /// <summary>
        /// Registra todos los servicios del motor MEP en el contenedor.
        /// El contenedor debe traer ya registrado un ILoggerFactory.
        /// </summary>
        public static DiToolsServiceContainer AddCalcServices(this DiToolsServiceContainer container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            var loggerFactory = container.Resolve<ILoggerFactory>();

            container.Register<ICalcEngineService>(new CalcEngineService(loggerFactory));
            container.Register<ICriticalPathService>(new CalcCriticalPathService(loggerFactory));
            container.Register<IHeatmapService>(new CalcHeatmapService(loggerFactory));
            container.Register<ISchematicLayoutService>(new CalcSchematicLayoutService(loggerFactory));
            container.Register<ICalcFilterService>(new CalcFilterServiceImpl(loggerFactory));

            return container;
        }
    }
}
