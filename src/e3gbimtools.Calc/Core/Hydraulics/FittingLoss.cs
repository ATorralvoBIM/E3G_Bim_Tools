// ============================================================================
//  FittingLoss.cs
//  Core.Hydraulics — Pérdida singular en un accesorio (K · 1/2 · rho · v^2).
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.4.
//  Tabla de coeficientes K extraída del briefing (Crane TP-410 / ASHRAE HOF).
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;

namespace e3gbimtools.Calc.Core.Hydraulics
{
    /// <summary>
    /// Pérdida singular en un accesorio (codo, té, válvula, reducción).
    /// La pérdida se calcula como  ΔP = K * 1/2 * rho * v^2  [Pa].
    /// </summary>
    public class FittingLoss
    {
        /// <summary>Tipo del accesorio: clave de FittingLossDatabase ("elbow90_long", ...).</summary>
        public string FittingType { get; set; }

        /// <summary>Coeficiente de resistencia local K (adimensional).</summary>
        public double KCoefficient { get; set; }

        /// <summary>Id del elemento Revit del accesorio (FamilyInstance, fitting).</summary>
        public long RevitFittingId { get; set; }

        /// <summary>Etiqueta humana opcional para informes.</summary>
        public string Label { get; set; }

        /// <summary>
        /// Pérdida de carga generada por este accesorio para una velocidad y
        /// densidad dadas.  ΔP = K * 1/2 * rho * v^2  [Pa].
        /// </summary>
        public double CalculateLossPa(double velocityMs, double densityKgM3)
        {
            return KCoefficient * densityKgM3 * velocityMs * velocityMs / 2.0;
        }

        public override string ToString()
        {
            string lbl = string.IsNullOrEmpty(Label) ? FittingType : Label;
            return lbl + " (K=" + KCoefficient.ToString("0.00") + ")";
        }

        // -- Factories ------------------------------------------------------

        public static FittingLoss Of(string fittingType)
        {
            return new FittingLoss
            {
                FittingType  = fittingType,
                KCoefficient = FittingLossDatabase.GetK(fittingType),
            };
        }

        /// <summary>
        /// Té de paso directo cuya pérdida depende del cociente de caudal
        /// β = Q_ramal/Q_entrada (Sprint 8 punto 2).
        /// </summary>
        public static FittingLoss OfTeeThrough(double flowRatio)
        {
            double k = FittingLossDatabase.GetTeeThroughK(flowRatio);
            return new FittingLoss
            {
                FittingType  = "tee_through",
                KCoefficient = k,
                Label        = "T paso directo (β=" + flowRatio.ToString("0.00") + ")",
            };
        }

        /// <summary>
        /// Té de derivación cuya pérdida depende del cociente de caudal
        /// β = Q_ramal/Q_entrada (Sprint 8 punto 2).
        /// </summary>
        public static FittingLoss OfTeeBranch(double flowRatio)
        {
            double k = FittingLossDatabase.GetTeeBranchK(flowRatio);
            return new FittingLoss
            {
                FittingType  = "tee_branch",
                KCoefficient = k,
                Label        = "T derivación (β=" + flowRatio.ToString("0.00") + ")",
            };
        }
    }

    /// <summary>
    /// Base de datos de coeficientes K típicos para los accesorios más
    /// comunes en redes de tuberías.  Valores medios; la v2 integrará
    /// catálogos de fabricante con K específicos.
    /// </summary>
    public static class FittingLossDatabase
    {
        /// <summary>Diccionario clave -> K (valores del briefing § 3.4).</summary>
        private static readonly Dictionary<string, double> _k =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            // Codos
            { "elbow90_short",       1.5   },  // Codo 90° radio corto
            { "elbow90_long",        0.7   },  // Codo 90° radio largo (R >= 1.5 D)
            { "elbow45",             0.4   },  // Codo 45°
            { "elbow_return",        2.2   },  // Codo de retorno 180°

            // Tés
            { "tee_through",         0.5   },  // T paso directo (media)
            { "tee_branch",          1.3   },  // T derivación (media)

            // Válvulas
            { "valve_ball_open",     0.15  },  // Válvula de bola abierta
            { "valve_gate_open",     0.20  },  // Válvula de compuerta abierta
            { "valve_check",         3.0   },  // Válvula de retención
            { "valve_butterfly_open",0.65  },  // Válvula de mariposa abierta
            { "valve_globe_open",    6.0   },  // Válvula globo (asiento) abierta

            // Reducciones / ampliaciones
            { "reducer_gradual",     0.05  },  // Reducción gradual cono <30°
            { "reducer_abrupt",      0.45  },  // Reducción brusca
            { "expander_gradual",    0.3   },  // Ampliación cónica
            { "expander_abrupt",     1.0   },  // Ampliación brusca

            // Entradas / salidas
            { "entry_sharp",         0.5   },  // Entrada de depósito borde vivo
            { "entry_rounded",       0.05  },  // Entrada bien redondeada
            { "exit_to_tank",        1.0   },  // Salida a depósito
        };

        private static readonly Dictionary<string, string> _displayName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "elbow90_short",       "Codo 90° radio corto" },
            { "elbow90_long",        "Codo 90° radio largo" },
            { "elbow45",             "Codo 45°" },
            { "elbow_return",        "Codo de retorno 180°" },
            { "tee_through",         "T paso directo" },
            { "tee_branch",          "T derivación" },
            { "valve_ball_open",     "Válvula de bola abierta" },
            { "valve_gate_open",     "Válvula de compuerta abierta" },
            { "valve_check",         "Válvula de retención" },
            { "valve_butterfly_open","Válvula de mariposa abierta" },
            { "valve_globe_open",    "Válvula globo (asiento) abierta" },
            { "reducer_gradual",     "Reducción gradual" },
            { "reducer_abrupt",      "Reducción brusca" },
            { "expander_gradual",    "Ampliación gradual" },
            { "expander_abrupt",     "Ampliación brusca" },
            { "entry_sharp",         "Entrada borde vivo" },
            { "entry_rounded",       "Entrada redondeada" },
            { "exit_to_tank",        "Salida a depósito" },
        };

        /// <summary>Devuelve K del accesorio o 0 si la clave no existe.</summary>
        public static double GetK(string fittingType)
        {
            if (string.IsNullOrEmpty(fittingType)) return 0.0;
            return _k.TryGetValue(fittingType, out var v) ? v : 0.0;
        }

        // ------------------------------------------------------------------
        // [Sprint 8 punto 2] K de tés dependiente del cociente de caudal.
        //
        // Crane TP-410 § 3 y ASHRAE HOF cap. 22 indican que la pérdida en un
        // té de paso directo o de derivación depende del cociente
        //   β = Q_ramal / Q_entrada    (0..1)
        // El briefing § 3.4 da los rangos:
        //   T paso directo   → K = 0.3 a 0.6
        //   T derivación     → K = 0.8 a 1.8
        //
        // Para v1 usamos parametrizaciones simples (cuadrática y lineal en β)
        // que reproducen los extremos del rango.  En v2 podrán sustituirse por
        // las curvas de catálogo de fabricantes.
        // ------------------------------------------------------------------

        /// <summary>
        /// K del paso directo (run) de un té cuando una fracción β del caudal
        /// entrante se desvía al ramal.  β=0 → K=0.3; β=1 → K=0.6.
        /// </summary>
        public static double GetTeeThroughK(double flowRatio)
        {
            double beta = ClampBeta(flowRatio);
            // K_run = 0.3 + 0.3·β²  (cuadrática — la pérdida crece más rápido
            // cuanto mayor es el caudal desviado).
            return 0.3 + 0.3 * beta * beta;
        }

        /// <summary>
        /// K de la derivación (branch) de un té cuando una fracción β del
        /// caudal entrante toma el ramal.  β=0 → K=0.8; β=1 → K=1.8.
        /// </summary>
        public static double GetTeeBranchK(double flowRatio)
        {
            double beta = ClampBeta(flowRatio);
            // K_branch = 0.8 + 1.0·β  (lineal — aproxima Crane TP-410 para
            // té estándar 90° con áreas iguales).
            return 0.8 + 1.0 * beta;
        }

        private static double ClampBeta(double beta)
            => double.IsNaN(beta) ? 0.0 : Math.Max(0.0, Math.Min(1.0, beta));

        /// <summary>Etiqueta humana para informes.  Si no existe devuelve la clave.</summary>
        public static string GetDisplayName(string fittingType)
        {
            if (string.IsNullOrEmpty(fittingType)) return "(sin tipo)";
            return _displayName.TryGetValue(fittingType, out var v) ? v : fittingType;
        }

        /// <summary>True si la clave está registrada.</summary>
        public static bool Contains(string fittingType)
            => !string.IsNullOrEmpty(fittingType) && _k.ContainsKey(fittingType);

        /// <summary>Enumera todas las claves de accesorio disponibles.</summary>
        public static IEnumerable<string> Keys => _k.Keys;
    }
}
