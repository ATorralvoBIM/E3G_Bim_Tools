// ============================================================================
//  FrictionFactor.cs
//  Core.Hydraulics — Factor de fricción de Darcy f por régimen de flujo.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.3.
//
//  Implementación:
//    - Re < 2300       : laminar           f = 64 / Re
//    - 2300 < Re < 4000: transitorio       interpolación lineal
//    - Re > 4000       : turbulento        Colebrook-White  (semilla
//                        Swamee-Jain + iteración Newton-Raphson sobre 1/sqrt(f))
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;

namespace E3G.Saneamiento.Core.Hydraulics
{
    /// <summary>
    /// Cálculo del factor de fricción de Darcy f.  Función pura y stateless.
    /// </summary>
    public static class FrictionFactor
    {
        /// <summary>Reynolds límite régimen laminar (inclusive).</summary>
        public const double ReLaminar = 2300.0;

        /// <summary>Reynolds inicio régimen plenamente turbulento.</summary>
        public const double ReTurbulent = 4000.0;

        /// <summary>Iteraciones máximas en Colebrook-White (típicamente converge en 3-5).</summary>
        public const int MaxIterations = 50;

        /// <summary>Tolerancia absoluta sobre 1/sqrt(f) en la iteración Colebrook.</summary>
        public const double Tolerance = 1e-10;

        /// <summary>
        /// Devuelve el factor de fricción de Darcy para los parámetros dados.
        /// </summary>
        /// <param name="re">Número de Reynolds (adimensional).  Re > 0.</param>
        /// <param name="roughness">Rugosidad absoluta del material [m].  >= 0.</param>
        /// <param name="diameter">Diámetro interior de la tubería [m].  > 0.</param>
        public static double Calculate(double re, double roughness, double diameter)
        {
            if (diameter <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(diameter), "Diámetro debe ser > 0.");
            if (re <= 0.0) return double.NaN;

            if (re < ReLaminar)
                return 64.0 / re;

            double fTurb = ColebrookWhite(re, roughness, diameter);

            if (re < ReTurbulent)
            {
                // Interpolación lineal entre el extremo laminar (64/2300) y el
                // valor Colebrook a Re = 4000.
                double fLam2300  = 64.0 / ReLaminar;
                double fTurb4000 = ColebrookWhite(ReTurbulent, roughness, diameter);
                double t = (re - ReLaminar) / (ReTurbulent - ReLaminar);
                return fLam2300 * (1.0 - t) + fTurb4000 * t;
            }

            return fTurb;
        }

        /// <summary>
        /// Aproximación explícita de Swamee-Jain.  Se usa como semilla
        /// inicial para la iteración Colebrook-White.
        ///   f ~ 0.25 / [log10( eps/(3.7 D) + 5.74 / Re^0.9 )]^2
        /// </summary>
        public static double SwameeJain(double re, double roughness, double diameter)
        {
            double relRough = roughness / diameter;
            double arg = relRough / 3.7 + 5.74 / Math.Pow(re, 0.9);
            double logArg = Math.Log10(arg);
            return 0.25 / (logArg * logArg);
        }

        /// <summary>
        /// Resolución de Colebrook-White:
        ///   1/sqrt(f) = -2 log10( eps/(3.71 D) + 2.51 / (Re * sqrt(f)) )
        /// Se trabaja sobre la variable g = 1/sqrt(f) que da una expresión más
        /// estable numéricamente.  Newton-Raphson + clamp positivo.
        /// </summary>
        public static double ColebrookWhite(double re, double roughness, double diameter)
        {
            double relRough = roughness / diameter;

            // Caso límite: tubería completamente lisa (rugosidad nula)
            if (relRough <= 0.0)
            {
                return PrandtlVonKarmanSmooth(re);
            }

            // Semilla con Swamee-Jain
            double f = SwameeJain(re, roughness, diameter);
            // g = 1 / sqrt(f)
            double g = 1.0 / Math.Sqrt(Math.Max(f, 1e-6));

            const double ln10 = 2.302585092994046;
            for (int i = 0; i < MaxIterations; i++)
            {
                double inner = relRough / 3.71 + 2.51 * g / re;
                double h     = g + 2.0 * Math.Log10(inner);
                if (Math.Abs(h) < Tolerance) break;
                // h'(g) = 1 + 2 * (2.51 / Re) / (ln(10) * inner)
                double hPrime = 1.0 + 2.0 * (2.51 / re) / (ln10 * inner);
                double step = h / hPrime;
                g -= step;
                if (g < 1.0) g = 1.0;        // clamp: f no puede pasar de 1
                if (g > 1e3) g = 1e3;        // clamp superior por seguridad
            }
            return 1.0 / (g * g);
        }

        /// <summary>
        /// Caso pared lisa (rugosidad ~ 0).  Solución de la ecuación de
        /// Prandtl - von Karman:  1/sqrt(f) = 2 log10( Re sqrt(f) ) - 0.8.
        /// </summary>
        public static double PrandtlVonKarmanSmooth(double re)
        {
            // Semilla Blasius (válida 4e3 < Re < 1e5):  f = 0.316 / Re^0.25
            double f = 0.316 / Math.Pow(re, 0.25);
            double g = 1.0 / Math.Sqrt(Math.Max(f, 1e-6));

            for (int i = 0; i < MaxIterations; i++)
            {
                double rhs = 2.0 * Math.Log10(re / g) - 0.8;
                double diff = g - rhs;
                if (Math.Abs(diff) < Tolerance) break;
                g = rhs;  // iteración fija (convergencia rápida)
                if (g < 1.0) g = 1.0;
            }
            return 1.0 / (g * g);
        }
    }
}
