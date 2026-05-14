// ============================================================================
//  Units.cs
//  Core.Common — Conversiones de unidades SI ↔ Revit (imperial).
//
//  Reglas:
//    - El núcleo de cálculo trabaja SIEMPRE en SI (m, kg, s, Pa, °C, m³/s).
//    - Revit almacena internamente en imperial (ft, lb, s, psi/inH2O, °F, gal/min
//      o CFM según la categoría). Esta clase concentra todos los factores.
//    - Esta clase NO referencia Autodesk.Revit.DB. Solo trabaja con doubles.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 6 (Reglas críticas, Regla 2).
// ============================================================================

using System;

namespace E3G.Saneamiento.Core.Common
{
    /// <summary>
    /// Conversiones de unidades entre Sistema Internacional (SI) y las unidades
    /// internas de Revit (imperial).  Todas las funciones son puras y stateless.
    /// </summary>
    public static class Units
    {
        // --------------------------------------------------------------------
        //  Constantes físicas y factores de conversión
        // --------------------------------------------------------------------

        /// <summary>1 ft expresado en metros (factor exacto por definición).</summary>
        public const double FootToMeter = 0.3048;

        /// <summary>1 m expresado en pies.</summary>
        public const double MeterToFoot = 1.0 / FootToMeter;

        /// <summary>1 in expresado en metros.</summary>
        public const double InchToMeter = 0.0254;

        /// <summary>1 m expresado en pulgadas.</summary>
        public const double MeterToInch = 1.0 / InchToMeter;

        /// <summary>1 mm expresado en metros.</summary>
        public const double MillimeterToMeter = 1e-3;

        /// <summary>1 m expresado en mm.</summary>
        public const double MeterToMillimeter = 1e3;

        /// <summary>1 ft² expresado en m².</summary>
        public const double SquareFootToSquareMeter = FootToMeter * FootToMeter;

        /// <summary>1 ft³ expresado en m³.</summary>
        public const double CubicFootToCubicMeter = FootToMeter * FootToMeter * FootToMeter;

        /// <summary>1 gal/min (US) expresado en m³/s.</summary>
        public const double GpmToCubicMeterPerSecond = 6.30901964e-5;

        /// <summary>1 m³/s expresado en gal/min.</summary>
        public const double CubicMeterPerSecondToGpm = 1.0 / GpmToCubicMeterPerSecond;

        /// <summary>1 CFM (ft³/min) expresado en m³/s.</summary>
        public const double CfmToCubicMeterPerSecond = CubicFootToCubicMeter / 60.0;

        /// <summary>1 m³/s expresado en CFM.</summary>
        public const double CubicMeterPerSecondToCfm = 1.0 / CfmToCubicMeterPerSecond;

        /// <summary>1 L/s expresado en m³/s.</summary>
        public const double LitersPerSecondToCubicMeterPerSecond = 1e-3;

        /// <summary>1 inH₂O (4°C) expresado en Pa.</summary>
        public const double InchWaterColumnToPa = 249.0889;

        /// <summary>1 mmH₂O (4°C) expresado en Pa.</summary>
        public const double MillimeterWaterColumnToPa = 9.80665;

        /// <summary>1 bar expresado en Pa.</summary>
        public const double BarToPa = 1e5;

        /// <summary>1 psi expresado en Pa.</summary>
        public const double PsiToPa = 6894.757293;

        /// <summary>
        /// Factor del briefing § 3.1 / § 4.1 para pérdida de carga lineal de Revit.
        /// Revit almacena la fricción en (inH₂O / 100 ft).  1 inH₂O/100ft = 8.1714 Pa/m.
        /// El briefing define la conversión inversa: revitValue × 0.04251 → Pa/m,
        /// pero el factor correcto entre esas unidades es 8.1714 (= 249.0889/30.48).
        /// Mantenemos la constante explícita y bidireccional para evitar ambigüedad.
        /// </summary>
        public const double InchWaterPer100FootToPaPerMeter = InchWaterColumnToPa / (100.0 * FootToMeter);

        /// <summary>1 Pa/m expresado en inH₂O / 100 ft.</summary>
        public const double PaPerMeterToInchWaterPer100Foot = 1.0 / InchWaterPer100FootToPaPerMeter;

        /// <summary>
        /// Factor literal del briefing v1.0: revitValue × 0.04251 ≈ Pa/m.
        /// Conservado por trazabilidad pero <see cref="InchWaterPer100FootToPaPerMeter"/>
        /// es la constante recomendada para nuevos cálculos.
        /// </summary>
        public const double BriefingFrictionFactor = 0.04251;

        // --------------------------------------------------------------------
        //  Longitud
        // --------------------------------------------------------------------

        public static double FtToM(double feet)        => feet * FootToMeter;
        public static double MToFt(double meters)      => meters * MeterToFoot;
        public static double InToM(double inches)      => inches * InchToMeter;
        public static double MToIn(double meters)      => meters * MeterToInch;
        public static double MmToM(double millimeters) => millimeters * MillimeterToMeter;
        public static double MToMm(double meters)      => meters * MeterToMillimeter;

        // --------------------------------------------------------------------
        //  Área y volumen
        // --------------------------------------------------------------------

        public static double SqFtToSqM(double squareFeet)       => squareFeet * SquareFootToSquareMeter;
        public static double SqMToSqFt(double squareMeters)     => squareMeters / SquareFootToSquareMeter;
        public static double CuFtToCuM(double cubicFeet)        => cubicFeet * CubicFootToCubicMeter;
        public static double CuMToCuFt(double cubicMeters)      => cubicMeters / CubicFootToCubicMeter;

        // --------------------------------------------------------------------
        //  Caudal
        // --------------------------------------------------------------------

        public static double GpmToM3s(double gpm)    => gpm * GpmToCubicMeterPerSecond;
        public static double M3sToGpm(double m3s)    => m3s * CubicMeterPerSecondToGpm;
        public static double CfmToM3s(double cfm)    => cfm * CfmToCubicMeterPerSecond;
        public static double M3sToCfm(double m3s)    => m3s * CubicMeterPerSecondToCfm;
        public static double LpsToM3s(double lps)    => lps * LitersPerSecondToCubicMeterPerSecond;
        public static double M3sToLps(double m3s)    => m3s / LitersPerSecondToCubicMeterPerSecond;

        // --------------------------------------------------------------------
        //  Velocidad
        // --------------------------------------------------------------------

        public static double FtSToMs(double feetPerSecond) => feetPerSecond * FootToMeter;
        public static double MsToFtS(double metersPerSec)  => metersPerSec * MeterToFoot;

        // --------------------------------------------------------------------
        //  Presión y pérdida de carga
        // --------------------------------------------------------------------

        public static double InWcToPa(double inchesWaterColumn)  => inchesWaterColumn * InchWaterColumnToPa;
        public static double PaToInWc(double pa)                 => pa / InchWaterColumnToPa;
        public static double MmWcToPa(double mmWaterColumn)      => mmWaterColumn * MillimeterWaterColumnToPa;
        public static double PaToMmWc(double pa)                 => pa / MillimeterWaterColumnToPa;
        public static double BarToPaUnit(double bar)             => bar * BarToPa;
        public static double PaToBar(double pa)                  => pa / BarToPa;
        public static double PsiToPaUnit(double psi)             => psi * PsiToPa;
        public static double PaToPsi(double pa)                  => pa / PsiToPa;

        /// <summary>Pérdida lineal Revit (inH₂O/100ft) → Pa/m.</summary>
        public static double InWcPer100FtToPaPerM(double inWcPer100Ft)
            => inWcPer100Ft * InchWaterPer100FootToPaPerMeter;

        /// <summary>Pérdida lineal Pa/m → inH₂O/100ft.</summary>
        public static double PaPerMToInWcPer100Ft(double paPerM)
            => paPerM * PaPerMeterToInchWaterPer100Foot;

        // --------------------------------------------------------------------
        //  Temperatura
        // --------------------------------------------------------------------

        public static double FahrenheitToCelsius(double f) => (f - 32.0) * 5.0 / 9.0;
        public static double CelsiusToFahrenheit(double c) => c * 9.0 / 5.0 + 32.0;
        public static double CelsiusToKelvin(double c)     => c + 273.15;
        public static double KelvinToCelsius(double k)     => k - 273.15;

        // --------------------------------------------------------------------
        //  Densidad
        // --------------------------------------------------------------------

        /// <summary>1 lb/ft³ expresado en kg/m³.</summary>
        public const double LbPerCuFtToKgPerCuM = 16.018463;

        public static double LbCuFtToKgM3(double lbPerCuFt) => lbPerCuFt * LbPerCuFtToKgPerCuM;
        public static double KgM3ToLbCuFt(double kgPerM3)   => kgPerM3 / LbPerCuFtToKgPerCuM;

        // --------------------------------------------------------------------
        //  Helpers semánticos para lectura/escritura desde la API de Revit
        // --------------------------------------------------------------------

        /// <summary>Convierte un valor de longitud leído de Revit (pies) a metros.</summary>
        public static double LengthFromRevit(double revitFeet) => revitFeet * FootToMeter;

        /// <summary>Convierte un valor de longitud SI (metros) al valor que espera Revit (pies).</summary>
        public static double LengthToRevit(double meters) => meters * MeterToFoot;

        /// <summary>Caudal de tubería Revit (gpm) → m³/s.</summary>
        public static double PipeFlowFromRevit(double revitGpm) => revitGpm * GpmToCubicMeterPerSecond;

        /// <summary>Caudal de tubería SI (m³/s) → gpm.</summary>
        public static double PipeFlowToRevit(double m3s) => m3s * CubicMeterPerSecondToGpm;

        /// <summary>Caudal de conducto Revit (CFM) → m³/s.</summary>
        public static double DuctFlowFromRevit(double revitCfm) => revitCfm * CfmToCubicMeterPerSecond;

        /// <summary>Caudal de conducto SI (m³/s) → CFM.</summary>
        public static double DuctFlowToRevit(double m3s) => m3s * CubicMeterPerSecondToCfm;

        /// <summary>Velocidad Revit (ft/s) → m/s.</summary>
        public static double VelocityFromRevit(double revitFtS) => revitFtS * FootToMeter;

        /// <summary>Velocidad SI (m/s) → ft/s.</summary>
        public static double VelocityToRevit(double ms) => ms * MeterToFoot;

        /// <summary>Pérdida de carga lineal Revit (inH₂O/100ft) → Pa/m.</summary>
        public static double FrictionFromRevit(double revitInWcPer100Ft)
            => revitInWcPer100Ft * InchWaterPer100FootToPaPerMeter;

        /// <summary>Pérdida de carga lineal SI (Pa/m) → inH₂O/100ft.</summary>
        public static double FrictionToRevit(double paPerM)
            => paPerM * PaPerMeterToInchWaterPer100Foot;

        // --------------------------------------------------------------------
        //  Validación numérica
        // --------------------------------------------------------------------

        /// <summary>Devuelve true si el valor es finito y no NaN.</summary>
        public static bool IsValid(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Lanza si el valor no es positivo finito.</summary>
        public static double RequirePositive(double value, string paramName)
        {
            if (!IsValid(value) || value <= 0.0)
                throw new ArgumentOutOfRangeException(paramName, value, "Se requiere un valor positivo finito.");
            return value;
        }

        /// <summary>Lanza si el valor es negativo o no finito.</summary>
        public static double RequireNonNegative(double value, string paramName)
        {
            if (!IsValid(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(paramName, value, "Se requiere un valor ≥ 0 finito.");
            return value;
        }
    }
}
