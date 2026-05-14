// ============================================================================
//  AirProperties.cs
//  Core.Aeraulics — Propiedades del aire húmedo / seco con corrección por
//  altitud para cálculo aerodinámico de conductos.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 4.3.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using E3G.Saneamiento.Core.Common;

namespace E3G.Saneamiento.Core.Aeraulics
{
    /// <summary>
    /// Propiedades del aire seco a presión y altitud arbitrarias.
    /// Funciones puras stateless.
    /// </summary>
    public static class AirProperties
    {
        /// <summary>Presión atmosférica al nivel del mar [Pa].</summary>
        public const double SeaLevelPressurePa = 101325.0;

        /// <summary>Constante específica del aire seco R [J/(kg·K)].</summary>
        public const double SpecificGasConstant = 287.058;

        /// <summary>Aceleración gravitacional [m/s²].</summary>
        public const double Gravity = 9.80665;

        /// <summary>
        /// Presión barométrica estándar a una altitud (modelo ISA).
        ///   p(h) = p0 (1 - 2.25577e-5 h)^5.25588   [Pa]
        /// </summary>
        public static double PressureAtAltitude(double altitudeM)
        {
            return SeaLevelPressurePa * Math.Pow(1.0 - 2.25577e-5 * altitudeM, 5.25588);
        }

        /// <summary>
        /// Densidad del aire seco a temperatura T [°C] y altitud [m] usando
        /// la ecuación de gas ideal:  rho = p / (R · T_K).
        /// </summary>
        public static double Density(double tempC, double altitudeM = 0.0)
        {
            double p  = PressureAtAltitude(altitudeM);
            double tK = tempC + 273.15;
            return p / (SpecificGasConstant * tK);
        }

        /// <summary>
        /// Viscosidad dinámica del aire por la formula de Sutherland:
        ///   mu = 1.458e-6 · T^1.5 / (T + 110.4)   [Pa·s], T en K.
        /// </summary>
        public static double DynamicViscosity(double tempC)
        {
            double tK = tempC + 273.15;
            return 1.458e-6 * Math.Pow(tK, 1.5) / (tK + 110.4);
        }

        /// <summary>Calor específico cp del aire seco [J/(kg·K)] (constante en HVAC).</summary>
        public const double SpecificHeat = 1005.0;

        /// <summary>Conductividad térmica del aire [W/(m·K)] (interpolación lineal con T).</summary>
        public static double ThermalConductivity(double tempC) => 0.0241 + 7.5e-5 * tempC;

        /// <summary>
        /// Devuelve un FluidProperties consolidado para usar con
        /// HydraulicCalculator/AeraulicCalculator.
        /// </summary>
        public static FluidProperties AsFluid(double tempC, double altitudeM = 0.0)
        {
            return new FluidProperties
            {
                FluidType           = FluidType.Air,
                Label               = "Aire " + tempC.ToString("0.#") + " C, alt=" + altitudeM.ToString("0") + " m",
                TemperatureC        = tempC,
                Density             = Density(tempC, altitudeM),
                DynamicViscosity    = DynamicViscosity(tempC),
                SpecificHeat        = SpecificHeat,
                ThermalConductivity = ThermalConductivity(tempC),
            };
        }

        /// <summary>Aire estandar 20 °C a nivel del mar.</summary>
        public static FluidProperties Standard => AsFluid(20.0, 0.0);
    }
}
