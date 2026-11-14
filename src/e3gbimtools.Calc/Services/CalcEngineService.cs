// ============================================================================
//  CalcEngineService.cs
//  Implementacion de ICalcEngineService.  Delega en HydraulicCalculator y
//  AeraulicCalculator.  Logging via ILogger.  No usa ITransactionService
//  porque el motor solo computa (no escribe en el modelo) - la escritura
//  de resultados a parametros compartidos vivira en FASE 5 dentro del
//  comando que la requiera, envuelta en un unico RunGroup.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Core.Aeraulics;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;

namespace e3gbimtools.Calc.Services
{
    public sealed class CalcEngineService : ICalcEngineService
    {
        private readonly ILogger _log;

        public CalcEngineService(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _log = loggerFactory.CreateLogger<CalcEngineService>();
        }

        public Task<CalcReport> CalculatePipingAsync(Document doc, PipeNetworkRequest req, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (req.Network == null) throw new ArgumentException("PipeNetworkRequest.Network es null.", nameof(req));
            if (req.Fluid == null)   throw new ArgumentException("PipeNetworkRequest.Fluid es null.", nameof(req));

            int segCount = req.Network.Segments != null ? req.Network.Segments.Count : 0;
            _log.Information("Calculo hidraulico iniciado: {Segments} tramos.", segCount);

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var calc = new HydraulicCalculator();
                    var report = calc.RunFullCalculation(req.Network, req.Fluid, req.Criteria);
                    _log.Information("Calculo hidraulico completado: {Errors} errores, {Warnings} avisos.",
                        report.CountErrors, report.CountWarnings);
                    return report;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Error(ex, "Calculo hidraulico fallido.");
                    throw;
                }
            }, ct);
        }

        public Task<CalcReport> CalculateDuctAsync(Document doc, DuctNetworkRequest req, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (req.Network == null) throw new ArgumentException("DuctNetworkRequest.Network es null.", nameof(req));
            if (req.Fluid == null)   throw new ArgumentException("DuctNetworkRequest.Fluid es null.", nameof(req));

            int segCount = req.Network.Segments != null ? req.Network.Segments.Count : 0;
            _log.Information("Calculo aeraulico iniciado: {Segments} tramos.", segCount);

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var calc = new AeraulicCalculator();
                    var report = calc.RunFullCalculation(req.Network, req.Fluid, req.Criteria);
                    _log.Information("Calculo aeraulico completado: {Errors} errores, {Warnings} avisos.",
                        report.CountErrors, report.CountWarnings);
                    return report;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Error(ex, "Calculo aeraulico fallido.");
                    throw;
                }
            }, ct);
        }
    }
}
