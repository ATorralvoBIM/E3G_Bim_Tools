// ============================================================================
//  DuctFittingLoss.cs
//  Core.Aeraulics — Pérdida singular en accesorios de conductos.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 4.4.
//  Misma metodologia K que tuberias pero con valores especificos para HVAC
//  (codos chapa, derivaciones T, transiciones, rejillas, atenuadores).
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;

namespace e3gbimtools.Calc.Core.Aeraulics
{
    /// <summary>
    /// Pérdida singular en un accesorio de conducto.  ΔP = K·½ρv².
    /// </summary>
    public class DuctFittingLoss
    {
        /// <summary>Tipo del accesorio (clave de DuctFittingLossDatabase).</summary>
        public string FittingType { get; set; }

        /// <summary>Coeficiente de resistencia local K (adimensional).</summary>
        public double KCoefficient { get; set; }

        /// <summary>Id del elemento Revit del accesorio.</summary>
        public long RevitFittingId { get; set; }

        /// <summary>Etiqueta humana opcional.</summary>
        public string Label { get; set; }

        /// <summary>ΔP = K · ½ρv² [Pa].</summary>
        public double CalculateLossPa(double velocityMs, double densityKgM3)
        {
            return KCoefficient * densityKgM3 * velocityMs * velocityMs / 2.0;
        }

        public override string ToString()
        {
            string lbl = string.IsNullOrEmpty(Label) ? FittingType : Label;
            return lbl + " (K=" + KCoefficient.ToString("0.00") + ")";
        }

        public static DuctFittingLoss Of(string fittingType)
        {
            return new DuctFittingLoss
            {
                FittingType  = fittingType,
                KCoefficient = DuctFittingLossDatabase.GetK(fittingType),
            };
        }

        /// <summary>
        /// Té de paso directo cuyo K depende del cociente Q_ramal/Q_entrada.
        /// (Sprint 8 punto 2).
        /// </summary>
        public static DuctFittingLoss OfTeeThrough(double flowRatio)
        {
            double k = DuctFittingLossDatabase.GetTeeThroughK(flowRatio);
            return new DuctFittingLoss
            {
                FittingType  = "tee_through",
                KCoefficient = k,
                Label        = "T paso directo (β=" + flowRatio.ToString("0.00") + ")",
            };
        }

        /// <summary>
        /// Té derivación cuyo K depende del cociente Q_ramal/Q_entrada.
        /// (Sprint 8 punto 2).
        /// </summary>
        public static DuctFittingLoss OfTeeBranch(double flowRatio)
        {
            double k = DuctFittingLossDatabase.GetTeeBranchK(flowRatio);
            return new DuctFittingLoss
            {
                FittingType  = "tee_branch",
                KCoefficient = k,
                Label        = "T derivación (β=" + flowRatio.ToString("0.00") + ")",
            };
        }
    }

    /// <summary>
    /// Base de datos de coeficientes K para accesorios de conductos.
    /// Valores medios extraidos de ASHRAE HOF y SMACNA.
    /// </summary>
    public static class DuctFittingLossDatabase
    {
        private static readonly Dictionary<string, double> _k =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            // Codos circulares
            { "elbow_circ_r1.5d",        0.20  },  // R = 1.5 D, suave
            { "elbow_circ_r1.0d",        0.33  },  // R = 1.0 D
            { "elbow_circ_segments",     0.50  },  // codo en gajos

            // Codos rectangulares
            { "elbow_rect_no_vanes",     1.20  },  // chapa sin guías
            { "elbow_rect_with_vanes",   0.30  },  // con guías directrices
            { "elbow_rect_radius",       0.30  },  // de radio (no en angulo recto)

            // Derivaciones (T) — flujo paso directo
            { "tee_through_low_branch",  0.10  },  // paso directo, ramal bajo flujo
            { "tee_through",             0.20  },  // paso directo medio
            { "tee_through_high_branch", 0.30  },  // paso directo, ramal alto flujo

            // Derivaciones (T) — flujo por el ramal
            { "tee_branch_low",          0.50  },  // ramal con poco flujo
            { "tee_branch",              0.85  },  // ramal medio
            { "tee_branch_high",         1.20  },  // ramal con mucho flujo

            // Transiciones
            { "reducer_gradual",         0.07  },  // angulo conico < 30 deg
            { "reducer_abrupt",          0.30  },
            { "expander_gradual",        0.40  },  // difusor 15 deg
            { "expander_abrupt",         1.00  },

            // Entradas y salidas
            { "entry_grille",            1.50  },  // rejilla impulsion (medio)
            { "entry_louver",            1.00  },  // lama exterior
            { "entry_sharp",             0.50  },
            { "exit_open",               1.00  },
            { "exit_grille",             1.50  },

            // Equipos en línea
            { "silencer",                10.0  },  // atenuador acustico (variable, ~5-20)
            { "filter_clean",            50.0  },  // filtro limpio (alto K)
            { "damper_open",             0.20  },  // compuerta abierta
            { "damper_partial",          5.00  },  // compuerta a 45 deg
            { "fire_damper_open",        0.30  },
        };

        private static readonly Dictionary<string, string> _displayName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "elbow_circ_r1.5d",        "Codo circular R=1.5D" },
            { "elbow_circ_r1.0d",        "Codo circular R=1.0D" },
            { "elbow_circ_segments",     "Codo circular en gajos" },
            { "elbow_rect_no_vanes",     "Codo rectangular sin guias" },
            { "elbow_rect_with_vanes",   "Codo rectangular con guias" },
            { "elbow_rect_radius",       "Codo rectangular de radio" },
            { "tee_through_low_branch",  "T paso directo (ramal bajo)" },
            { "tee_through",             "T paso directo" },
            { "tee_through_high_branch", "T paso directo (ramal alto)" },
            { "tee_branch_low",          "T ramal (bajo)" },
            { "tee_branch",              "T ramal" },
            { "tee_branch_high",         "T ramal (alto)" },
            { "reducer_gradual",         "Reduccion gradual" },
            { "reducer_abrupt",          "Reduccion brusca" },
            { "expander_gradual",        "Ampliacion gradual" },
            { "expander_abrupt",         "Ampliacion brusca" },
            { "entry_grille",            "Rejilla de impulsion" },
            { "entry_louver",            "Lama exterior" },
            { "entry_sharp",             "Entrada borde vivo" },
            { "exit_open",               "Salida libre" },
            { "exit_grille",             "Rejilla de retorno" },
            { "silencer",                "Atenuador acustico" },
            { "filter_clean",            "Filtro limpio" },
            { "damper_open",             "Compuerta abierta" },
            { "damper_partial",          "Compuerta a 45 deg" },
            { "fire_damper_open",        "Compuerta cortafuegos abierta" },
        };

        /// <summary>K del accesorio. 0 si la clave no existe.</summary>
        public static double GetK(string fittingType)
        {
            if (string.IsNullOrEmpty(fittingType)) return 0.0;
            return _k.TryGetValue(fittingType, out var v) ? v : 0.0;
        }

        /// <summary>Etiqueta humana.</summary>
        public static string GetDisplayName(string fittingType)
        {
            if (string.IsNullOrEmpty(fittingType)) return "(sin tipo)";
            return _displayName.TryGetValue(fittingType, out var v) ? v : fittingType;
        }

        public static bool Contains(string fittingType)
            => !string.IsNullOrEmpty(fittingType) && _k.ContainsKey(fittingType);

        public static IEnumerable<string> Keys => _k.Keys;

        // ------------------------------------------------------------------
        // [Sprint 8 punto 2] K de tés dependiente del cociente de caudal β.
        //
        // ASHRAE HOF cap. 22 — para tés rectangulares con áreas iguales:
        //   K_run    ≈ 0.10 + 0.20·β²    (paso directo)
        //   K_branch ≈ 0.50 + 0.70·β     (derivación)
        // donde β = Q_ramal / Q_entrada (0..1).
        //
        // Los valores extremos (β=0 y β=1) coinciden con las claves
        // "tee_through_low_branch / _high_branch" y "tee_branch_low / _high"
        // que ya estaban en la tabla — quedan como anclas legacy.
        // ------------------------------------------------------------------

        /// <summary>K de paso directo en T variable con el cociente Q_ramal/Q_entrada.</summary>
        public static double GetTeeThroughK(double flowRatio)
        {
            double beta = ClampBeta(flowRatio);
            return 0.10 + 0.20 * beta * beta;
        }

        /// <summary>K de derivación en T variable con el cociente Q_ramal/Q_entrada.</summary>
        public static double GetTeeBranchK(double flowRatio)
        {
            double beta = ClampBeta(flowRatio);
            return 0.50 + 0.70 * beta;
        }

        private static double ClampBeta(double beta)
            => double.IsNaN(beta) ? 0.0 : Math.Max(0.0, Math.Min(1.0, beta));
    }
}
