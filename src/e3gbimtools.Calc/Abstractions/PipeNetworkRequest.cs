// ============================================================================
//  PipeNetworkRequest.cs
//  DTO de entrada al ICalcEngineService.CalculatePipingAsync.
//  Empaqueta la red hidraulica ya construida desde Revit, el fluido y los
//  criterios de dimensionado.  La construccion de la red (lectura del Document)
//  la hace el caller en el hilo principal de Revit; el motor solo computa.
// ============================================================================

using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;

namespace e3gbimtools.Calc.Abstractions
{
    public sealed class PipeNetworkRequest
    {
        /// <summary>Red ya construida (segmentos, nodos, conexiones).</summary>
        public PipeNetwork Network { get; set; }

        /// <summary>Fluido (agua fria, agua caliente, etc.) con T, rho, mu.</summary>
        public FluidProperties Fluid { get; set; }

        /// <summary>Criterios CTE/RITE o personalizados.  Si es null, no dimensiona.</summary>
        public SizingCriteriaConfig Criteria { get; set; }
    }
}
