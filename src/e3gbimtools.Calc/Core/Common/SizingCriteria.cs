// ============================================================================
//  SizingCriteria.cs
//  Core.Common — Enumeraciones y configuración de criterios de dimensionado.
//
//  Briefing técnico v1.0 — Mayo 2026:
//    - Sección 5: enums SizingCriteria y FluidType.
//    - Sección 3.5: criterios de dimensionado para tuberías (RITE / CTE).
//    - Sección 4.2: métodos de dimensionado para conductos (igual fricción / velocidad).
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;

namespace e3gbimtools.Calc.Core.Common
{
    /// <summary>Criterio principal con el que se dimensiona la red.</summary>
    public enum SizingCriteria
    {
        /// <summary>Limita la velocidad máxima del fluido en cada tramo.</summary>
        MaxVelocity,
        /// <summary>Limita la pérdida de carga unitaria (Pa/m) en cada tramo.</summary>
        MaxFriction,
        /// <summary>Velocidad económica (mínimo coste vida útil).</summary>
        Economic
    }

    /// <summary>Tipo de fluido transportado.</summary>
    public enum FluidType
    {
        Water,
        GlycolMix,
        Air,
        OtherGas
    }

    /// <summary>Normativa de referencia aplicada al dimensionado.</summary>
    public enum SizingStandard
    {
        /// <summary>Sin normativa explícita (valores definidos por el usuario).</summary>
        Custom,
        /// <summary>RITE (España) — Reglamento de Instalaciones Térmicas en Edificios.</summary>
        RITE,
        /// <summary>CTE DB-HS4 (España) — Código Técnico, suministro de agua.</summary>
        CTE_HS4,
        /// <summary>ASHRAE Handbook of Fundamentals.</summary>
        ASHRAE
    }

    /// <summary>Tipo de servicio del sistema (afecta a los límites por defecto).</summary>
    public enum ServiceType
    {
        ColdWater,        // AF
        HotWater,         // ACS
        Recirculation,    // Retorno ACS
        Heating,          // Calefacción
        Cooling,          // Refrigeración / agua fría climatización
        SupplyAir,        // Impulsión HVAC
        ReturnAir,        // Retorno HVAC
        ExhaustAir,       // Extracción
        Other
    }

    /// <summary>
    /// Perfil de simultaneidad aplicado al caudal segun tipo de edificio.
    /// La formula para K (factor de simultaneidad) varia entre perfiles.
    /// El caudal de diseno de cada tramo se calcula como Q_diseno = Q_raw * K(n)
    /// donde n es el numero de aparatos aguas-abajo del tramo.
    /// </summary>
    public enum SimultaneityProfile
    {
        /// <summary>Sin simultaneidad: Q_diseno = Q_raw (suma directa).</summary>
        None,
        /// <summary>Vivienda (CTE/UNE clasico): K = 1/sqrt(n-1) para n>=2.</summary>
        Residential,
        /// <summary>Oficinas: K = 1/sqrt(n-1) con K minimo = 0.20.</summary>
        Office,
        /// <summary>Hospital/clinica: K = 1.7/sqrt(n).  Mas conservador.</summary>
        Hospital,
        /// <summary>Hotel: K = 1/sqrt(n-1) con K minimo = 0.25.</summary>
        Hotel,
        /// <summary>Comercial/restaurante: K = 1.5/sqrt(n).  Picos altos.</summary>
        Commercial
    }

    /// <summary>
    /// Configuración completa del dimensionado: criterio, normativa, límites y
    /// factor de simultaneidad.  Se construye normalmente con un preset y luego
    /// el usuario ajusta los valores en la UI.
    /// </summary>
    public class SizingCriteriaConfig
    {
        public SizingCriteria Criterion        { get; set; } = SizingCriteria.MaxVelocity;
        public SizingStandard Standard         { get; set; } = SizingStandard.RITE;
        public ServiceType    Service          { get; set; } = ServiceType.ColdWater;
        public FluidType      Fluid            { get; set; } = FluidType.Water;
        public double         FluidTemperatureC { get; set; } = 20.0;

        /// <summary>Velocidad máxima admisible [m/s] (criterio MaxVelocity).</summary>
        public double MaxVelocityMs { get; set; } = 2.0;

        /// <summary>Velocidad mínima recomendable [m/s] (estancamiento, sedimentación).</summary>
        public double MinVelocityMs { get; set; } = 0.5;

        /// <summary>Pérdida de carga unitaria máxima [Pa/m] (criterio MaxFriction).</summary>
        public double MaxFrictionPaPm { get; set; } = 200.0;

        /// <summary>
        /// Perfil de simultaneidad aplicado al caudal por tramo.  Calculado
        /// dinamicamente durante el calculo (n = aparatos aguas-abajo).
        /// </summary>
        public SimultaneityProfile SimultaneityProfile { get; set; } = SimultaneityProfile.None;

        /// <summary>Factor de simultaneidad fijo (legacy / override manual).  No usado si Profile != None.</summary>
        public double SimultaneityFactor { get; set; } = 1.0;

        /// <summary>Margen de seguridad sobre la pérdida total de la red [%].</summary>
        public double SafetyMarginPercent { get; set; } = 10.0;

        /// <summary>Texto descriptivo del criterio (informes, UI).</summary>
        public string Description { get; set; }

        /// <summary>Crea una copia profunda editable del criterio.</summary>
        public SizingCriteriaConfig Clone() => (SizingCriteriaConfig)MemberwiseClone();

        // --------------------------------------------------------------------
        //  Calculo del factor de simultaneidad por perfil
        // --------------------------------------------------------------------

        /// <summary>
        /// Devuelve el factor de simultaneidad K para n aparatos aguas-abajo,
        /// segun el perfil de edificio.  K en [0, 1].  Para n &lt;= 1 K = 1.
        /// </summary>
        public static double SimultaneityFactorOf(int n, SimultaneityProfile profile)
        {
            if (n <= 1) return 1.0;
            double k;
            switch (profile)
            {
                case SimultaneityProfile.None:
                    return 1.0;
                case SimultaneityProfile.Residential:
                    k = 1.0 / System.Math.Sqrt(n - 1);
                    break;
                case SimultaneityProfile.Office:
                    k = 1.0 / System.Math.Sqrt(n - 1);
                    if (k < 0.20) k = 0.20;
                    break;
                case SimultaneityProfile.Hospital:
                    k = 1.7 / System.Math.Sqrt(n);
                    break;
                case SimultaneityProfile.Hotel:
                    k = 1.0 / System.Math.Sqrt(n - 1);
                    if (k < 0.25) k = 0.25;
                    break;
                case SimultaneityProfile.Commercial:
                    k = 1.5 / System.Math.Sqrt(n);
                    break;
                default:
                    k = 1.0;
                    break;
            }
            if (k > 1.0) k = 1.0;
            if (k < 0.0) k = 0.0;
            return k;
        }

        // --------------------------------------------------------------------
        //  Presets normativos
        // --------------------------------------------------------------------

        /// <summary>Preset CTE DB-HS4 — Agua fría sanitaria, 1 m/s ramales / 1.5 m/s montantes.</summary>
        public static SizingCriteriaConfig CTE_ColdWater => new()
        {
            SimultaneityProfile = SimultaneityProfile.Residential,
            Criterion        = SizingCriteria.MaxVelocity,
            Standard         = SizingStandard.CTE_HS4,
            Service          = ServiceType.ColdWater,
            Fluid            = FluidType.Water,
            FluidTemperatureC = 15.0,
            MaxVelocityMs    = 2.0,
            MinVelocityMs    = 0.5,
            MaxFrictionPaPm  = 400.0,
            Description      = "CTE DB-HS4 — Agua fría sanitaria"
        };

        /// <summary>Preset CTE DB-HS4 — ACS, v ≤ 1.5 m/s.</summary>
        public static SizingCriteriaConfig CTE_HotWater => new()
        {
            SimultaneityProfile = SimultaneityProfile.Residential,
            Criterion        = SizingCriteria.MaxVelocity,
            Standard         = SizingStandard.CTE_HS4,
            Service          = ServiceType.HotWater,
            Fluid            = FluidType.Water,
            FluidTemperatureC = 60.0,
            MaxVelocityMs    = 1.5,
            MinVelocityMs    = 0.5,
            MaxFrictionPaPm  = 300.0,
            Description      = "CTE DB-HS4 — ACS"
        };

        /// <summary>Preset CTE DB-HS4 — Retorno ACS, v ≤ 1.0 m/s.</summary>
        public static SizingCriteriaConfig CTE_Recirculation => new()
        {
            SimultaneityProfile = SimultaneityProfile.Residential,
            Criterion        = SizingCriteria.MaxVelocity,
            Standard         = SizingStandard.CTE_HS4,
            Service          = ServiceType.Recirculation,
            Fluid            = FluidType.Water,
            FluidTemperatureC = 55.0,
            MaxVelocityMs    = 1.0,
            MinVelocityMs    = 0.3,
            MaxFrictionPaPm  = 250.0,
            Description      = "CTE DB-HS4 — Retorno ACS"
        };

        /// <summary>Preset RITE — Calefacción, ΔP/L ≤ 200 Pa/m.</summary>
        public static SizingCriteriaConfig RITE_Heating => new()
        {
            Criterion        = SizingCriteria.MaxFriction,
            Standard         = SizingStandard.RITE,
            Service          = ServiceType.Heating,
            Fluid            = FluidType.Water,
            FluidTemperatureC = 80.0,
            MaxVelocityMs    = 1.5,
            MaxFrictionPaPm  = 200.0,
            Description      = "RITE — Calefacción agua caliente"
        };

        /// <summary>Preset RITE — Climatización por agua fría.</summary>
        public static SizingCriteriaConfig RITE_Cooling => new()
        {
            Criterion        = SizingCriteria.MaxFriction,
            Standard         = SizingStandard.RITE,
            Service          = ServiceType.Cooling,
            Fluid            = FluidType.Water,
            FluidTemperatureC = 7.0,
            MaxVelocityMs    = 1.5,
            MaxFrictionPaPm  = 250.0,
            Description      = "RITE — Climatización agua fría"
        };

        /// <summary>Preset RITE — Conducto de impulsión principal (igual fricción 1.0 Pa/m).</summary>
        public static SizingCriteriaConfig RITE_SupplyDuct => new()
        {
            Criterion        = SizingCriteria.MaxFriction,
            Standard         = SizingStandard.RITE,
            Service          = ServiceType.SupplyAir,
            Fluid            = FluidType.Air,
            FluidTemperatureC = 20.0,
            MaxVelocityMs    = 8.0,
            MaxFrictionPaPm  = 1.0,
            Description      = "RITE — Conducto impulsión (igual fricción)"
        };

        /// <summary>Preset RITE — Conducto de retorno principal.</summary>
        public static SizingCriteriaConfig RITE_ReturnDuct => new()
        {
            Criterion        = SizingCriteria.MaxFriction,
            Standard         = SizingStandard.RITE,
            Service          = ServiceType.ReturnAir,
            Fluid            = FluidType.Air,
            FluidTemperatureC = 22.0,
            MaxVelocityMs    = 6.0,
            MaxFrictionPaPm  = 1.0,
            Description      = "RITE — Conducto retorno (igual fricción)"
        };

        /// <summary>Preset RITE — Conducto de extracción (WC, cocina), v ≤ 5 m/s.</summary>
        public static SizingCriteriaConfig RITE_ExhaustDuct => new()
        {
            Criterion        = SizingCriteria.MaxVelocity,
            Standard         = SizingStandard.RITE,
            Service          = ServiceType.ExhaustAir,
            Fluid            = FluidType.Air,
            FluidTemperatureC = 25.0,
            MaxVelocityMs    = 5.0,
            MaxFrictionPaPm  = 1.5,
            Description      = "RITE — Conducto extracción (WC/cocina)"
        };

        /// <summary>Preset ASHRAE — Conducto impulsión velocidad media.</summary>
        public static SizingCriteriaConfig ASHRAE_SupplyDuct => new()
        {
            Criterion        = SizingCriteria.MaxVelocity,
            Standard         = SizingStandard.ASHRAE,
            Service          = ServiceType.SupplyAir,
            Fluid            = FluidType.Air,
            FluidTemperatureC = 20.0,
            MaxVelocityMs    = 7.0,
            MaxFrictionPaPm  = 1.5,
            Description      = "ASHRAE — Conducto impulsión velocidad media"
        };

        /// <summary>
        /// Devuelve todos los presets disponibles, indexados por una clave estable
        /// que la UI puede usar para enlazar combos.
        /// </summary>
        public static IReadOnlyDictionary<string, SizingCriteriaConfig> Presets { get; } =
            new Dictionary<string, SizingCriteriaConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { "CTE_AF",          CTE_ColdWater },
                { "CTE_ACS",         CTE_HotWater },
                { "CTE_RECIRC",      CTE_Recirculation },
                { "RITE_HEATING",    RITE_Heating },
                { "RITE_COOLING",    RITE_Cooling },
                { "RITE_SUPPLY",     RITE_SupplyDuct },
                { "RITE_RETURN",     RITE_ReturnDuct },
                { "RITE_EXHAUST",    RITE_ExhaustDuct },
                { "ASHRAE_SUPPLY",   ASHRAE_SupplyDuct },
            };

        /// <summary>Obtiene un preset por clave.  Lanza si no existe.</summary>
        public static SizingCriteriaConfig FromPreset(string key)
        {
            if (Presets.TryGetValue(key, out var p)) return p.Clone();
            throw new KeyNotFoundException($"Preset de dimensionado desconocido: '{key}'.");
        }

        public override string ToString()
            => Description ?? $"{Standard} {Service} ({Criterion})";
    }
}
