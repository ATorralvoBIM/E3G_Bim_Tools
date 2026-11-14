// ============================================================================
//  DuctNetworkRequest.cs
//  DTO de entrada al ICalcEngineService.CalculateDuctAsync.
//  Empaqueta la red aeraulica ya construida desde Revit, el fluido (aire) y
//  los criterios de dimensionado.
// ============================================================================

using e3gbimtools.Calc.Core.Aeraulics;
using e3gbimtools.Calc.Core.Common;

namespace e3gbimtools.Calc.Abstractions
{
    public sealed class DuctNetworkRequest
    {
        /// <summary>Red de conductos ya construida.</summary>
        public DuctNetwork Network { get; set; }

        /// <summary>Aire (T, rho, mu) - usar AirProperties.Standard como default.</summary>
        public FluidProperties Fluid { get; set; }

        /// <summary>Criterios (igual velocidad / igual friccion).  Si es null, no dimensiona.</summary>
        public SizingCriteriaConfig Criteria { get; set; }
    }
}
