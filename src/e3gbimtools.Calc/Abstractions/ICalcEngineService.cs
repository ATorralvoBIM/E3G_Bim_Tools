// ============================================================================
//  ICalcEngineService.cs
//  Fachada del motor de calculo MEP.  Envuelve HydraulicCalculator y
//  AeraulicCalculator y los expone como operaciones async, para que la UI
//  (CalcWindow) pueda dispararlos sin bloquear el hilo de Revit.
//
//  Devuelve CalcReport (agregado por red), no CalcResult (que es la fila
//  individual de un tramo).  El briefing del usuario hablaba de "CalcResult"
//  pero el motor existente produce CalcReport y dentro lleva el diccionario
//  Segments<long, CalcResult>.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using e3gbimtools.Calc.Core.Common;

namespace e3gbimtools.Calc.Abstractions
{
    public interface ICalcEngineService
    {
        /// <summary>
        /// Ejecuta el calculo hidraulico completo (resolver red + dimensionar).
        /// El llamador construye la red en el hilo de Revit; este metodo
        /// solo computa y se puede invocar desde un hilo de fondo.
        /// </summary>
        Task<CalcReport> CalculatePipingAsync(Document doc, PipeNetworkRequest req, CancellationToken ct);

        /// <summary>
        /// Ejecuta el calculo aeraulico completo (resolver red + dimensionar).
        /// </summary>
        Task<CalcReport> CalculateDuctAsync(Document doc, DuctNetworkRequest req, CancellationToken ct);
    }
}
