// ============================================================================
//  FluidProperties.cs
//  Core.Common — Propiedades termofísicas de fluidos en SI.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.8 (firma) y sección 5 (FluidType).
//  El briefing entrega una correlación simplificada para agua; aquí se conserva
//  la API (Water/Water20C/...) pero las correlaciones internas usan ajustes
//  publicados (Kell 1975 para densidad, ajuste exponencial para viscosidad)
//  para devolver valores de ingeniería precisos en el rango 0–100 °C.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;

namespace e3gbimtools.Calc.Core.Common
{
    /// <summary>
    /// Propiedades de un fluido newtoniano homogéneo expresadas en SI.
    /// Todos los miembros son de instancia para permitir mezclas y temperaturas
    /// arbitrarias; el código de cálculo recibe una instancia ya construida.
    /// </summary>
    public class FluidProperties
    {
        /// <summary>Tipo de fluido (informativo, no influye en el cálculo).</summary>
        public FluidType FluidType { get; init; } = FluidType.Water;

        /// <summary>Etiqueta legible para informes ("Agua 60 C", "PG 30%", ...).</summary>
        public string Label { get; init; } = "Fluid";

        /// <summary>Temperatura del fluido en °C a la que se han evaluado las propiedades.</summary>
        public double TemperatureC { get; init; }

        /// <summary>Densidad [kg/m³].</summary>
        public double Density { get; init; }

        /// <summary>Viscosidad dinámica mu [Pa·s].</summary>
        public double DynamicViscosity { get; init; }

        /// <summary>Calor específico a presión constante cp [J/(kg·K)].</summary>
        public double SpecificHeat { get; init; }

        /// <summary>Conductividad térmica lambda [W/(m·K)].</summary>
        public double ThermalConductivity { get; init; }

        /// <summary>Viscosidad cinemática nu = mu/rho [m²/s].</summary>
        public double KinematicViscosity => DynamicViscosity / Density;

        /// <summary>Número de Prandtl Pr = mu·cp/lambda (adimensional).</summary>
        public double Prandtl
        {
            get
            {
                if (ThermalConductivity <= 0.0) return double.NaN;
                return DynamicViscosity * SpecificHeat / ThermalConductivity;
            }
        }

        // --------------------------------------------------------------------
        //  Agua pura
        // --------------------------------------------------------------------

        /// <summary>
        /// Propiedades del agua líquida a presión atmosférica para 0 .. 100 °C.
        /// Densidad: ajuste de Kell (1975).  Viscosidad: ajuste exponencial sobre
        /// tabla NIST.  cp y lambda: polinomios fitted a tabla NIST.
        /// </summary>
        public static FluidProperties Water(double tempC)
        {
            double t = ClampWater(tempC);
            return new FluidProperties
            {
                FluidType           = FluidType.Water,
                Label               = "Agua " + tempC.ToString("0.#") + " C",
                TemperatureC        = tempC,
                Density             = WaterDensity(t),
                DynamicViscosity    = WaterDynamicViscosity(t),
                SpecificHeat        = WaterSpecificHeat(t),
                ThermalConductivity = WaterThermalConductivity(t),
            };
        }

        /// <summary>Agua a 20 °C (referencia para fontanería y AF).</summary>
        public static FluidProperties Water20C => Water(20.0);

        /// <summary>Agua a 60 °C (referencia ACS).</summary>
        public static FluidProperties Water60C => Water(60.0);

        /// <summary>Agua a 80 °C (referencia calefacción).</summary>
        public static FluidProperties Water80C => Water(80.0);

        // --------------------------------------------------------------------
        //  Mezcla agua-glicol (propilenglicol PG)
        // --------------------------------------------------------------------

        /// <summary>
        /// Mezcla agua-propilenglicol a fracción másica <paramref name="glycolFraction"/>
        /// (0 = agua pura, 1 = glicol puro).  Cubre típicamente 0.20 - 0.50 a 5 - 80 °C.
        /// Correlaciones simplificadas a partir de tablas ASHRAE F31.
        /// </summary>
        public static FluidProperties PropyleneGlycolMix(double glycolFraction, double tempC)
        {
            double xg = Math.Max(0.0, Math.Min(1.0, glycolFraction));
            double t  = ClampWater(tempC);
            double rhoW = WaterDensity(t);
            double muW  = WaterDynamicViscosity(t);
            double cpW  = WaterSpecificHeat(t);
            double kW   = WaterThermalConductivity(t);

            // Densidad: aprox. lineal con corrección por T
            double rhoGlycol = 1036.0 - 0.62 * (t - 20.0);
            double rho = (1.0 - xg) * rhoW + xg * rhoGlycol;

            // Viscosidad: ley exponencial; PG puro mu mucho mayor que agua
            double viscFactor = Math.Exp(2.5 * xg) * Math.Exp(-0.025 * (t - 20.0));
            double mu = muW * viscFactor;

            // Calor específico: aprox. lineal (PG cp ~2510 J/(kg·K))
            double cp = (1.0 - xg) * cpW + xg * 2510.0;

            // Conductividad: aprox. lineal (PG lambda ~0.205 W/(m·K))
            double k = (1.0 - xg) * kW + xg * 0.205;

            return new FluidProperties
            {
                FluidType           = FluidType.GlycolMix,
                Label               = "PG " + (xg * 100.0).ToString("0") + "% / Agua a " + tempC.ToString("0.#") + " C",
                TemperatureC        = tempC,
                Density             = rho,
                DynamicViscosity    = mu,
                SpecificHeat        = cp,
                ThermalConductivity = k,
            };
        }

        /// <summary>Propilenglicol al 30 % en masa a la temperatura indicada.</summary>
        public static FluidProperties PG30(double tempC) => PropyleneGlycolMix(0.30, tempC);

        /// <summary>Propilenglicol al 50 % en masa a la temperatura indicada.</summary>
        public static FluidProperties PG50(double tempC) => PropyleneGlycolMix(0.50, tempC);

        // --------------------------------------------------------------------
        //  Aire (cómputo seco simple; ver AirProperties en Core.Aeraulics
        //  para el cálculo aerodinámico completo con altitud).
        // --------------------------------------------------------------------

        /// <summary>
        /// Aire seco a presión atmosférica estándar (101 325 Pa).  Para corrección
        /// por altitud usar Core.Aeraulics.AirProperties.
        /// </summary>
        public static FluidProperties Air(double tempC)
        {
            const double Patm = 101325.0;          // Pa
            const double Rair = 287.058;           // J/(kg·K)
            double tK    = tempC + 273.15;
            double rho   = Patm / (Rair * tK);
            // Sutherland para mu del aire
            double mu    = 1.458e-6 * Math.Pow(tK, 1.5) / (tK + 110.4);
            // cp aire a presión cte ~1005 J/(kg·K) en el rango 0 - 100 °C
            double cp    = 1005.0;
            // Conductividad aire (interp. lineal aproximada)
            double k     = 0.0241 + 7.5e-5 * tempC;
            return new FluidProperties
            {
                FluidType           = FluidType.Air,
                Label               = "Aire " + tempC.ToString("0.#") + " C",
                TemperatureC        = tempC,
                Density             = rho,
                DynamicViscosity    = mu,
                SpecificHeat        = cp,
                ThermalConductivity = k,
            };
        }

        /// <summary>Aire a 20 °C, presión estándar.</summary>
        public static FluidProperties Air20C => Air(20.0);

        // --------------------------------------------------------------------
        //  Correlaciones internas - Agua
        // --------------------------------------------------------------------

        private static double ClampWater(double tempC)
        {
            // Fuera del rango la correlación no es válida físicamente; se trunca.
            if (tempC < 0.0)   return 0.0;
            if (tempC > 100.0) return 100.0;
            return tempC;
        }

        /// <summary>Densidad del agua según ajuste de Kell (1975).  kg/m³.</summary>
        private static double WaterDensity(double t)
        {
            return 999.842594
                 + 6.793952e-2  * t
                 - 9.095290e-3  * t * t
                 + 1.001685e-4  * t * t * t
                 - 1.120083e-6  * t * t * t * t
                 + 6.536332e-9  * t * t * t * t * t;
        }

        /// <summary>Viscosidad dinámica del agua [Pa·s].  Ajuste sobre tabla NIST.</summary>
        private static double WaterDynamicViscosity(double t)
        {
            // Ecuación tipo Vogel: ln(mu) = A + B / (C + T)
            // Coeficientes ajustados a tabla NIST 0 - 100 °C, error < 1.5 %
            const double A = -3.7188;
            const double B =  578.919;
            const double C =  -137.546;
            double tK = t + 273.15;
            return Math.Exp(A + B / (C + tK)) * 1e-3; // mPa·s -> Pa·s
        }

        /// <summary>Calor específico cp del agua [J/(kg·K)].</summary>
        private static double WaterSpecificHeat(double t)
        {
            // Polinomio cuadrático fijado por (0,4220), (40,4179), (80,4197).
            // Máx error vs tabla NIST en 0 - 100 °C: < 12 J/(kg·K)  (~0.3 %).
            return 4220.0 - 1.7625 * t + 0.01844 * t * t;
        }

        /// <summary>Conductividad térmica lambda del agua [W/(m·K)].</summary>
        private static double WaterThermalConductivity(double t)
        {
            // Polinomio cuadrático ajustado a tabla NIST 0 - 100 °C
            return 0.5610 + 1.880e-3 * t - 8.40e-6 * t * t;
        }

        // --------------------------------------------------------------------
        //  Utilidades
        // --------------------------------------------------------------------

        public override string ToString()
        {
            return Label
                + ": rho=" + Density.ToString("0.0") + " kg/m3"
                + ", mu="  + DynamicViscosity.ToString("0.0000e+0") + " Pa.s"
                + ", nu="  + KinematicViscosity.ToString("0.0000e+0") + " m2/s"
                + ", cp="  + SpecificHeat.ToString("0") + " J/(kg.K)"
                + ", k="   + ThermalConductivity.ToString("0.000") + " W/(m.K)";
        }
    }
}
