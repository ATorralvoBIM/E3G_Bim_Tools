// ============================================================================
//  NominalDiameters.cs
//  Core.Hydraulics — Series de diámetros nominales (DN) estándar.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.5 (referencia
//  NominalDiameters.StandardSeries en la iteración de dimensionado).
//
//  Series cubiertas:
//    - Acero EN 10255 (medium series): DN15 ... DN300
//    - Cobre EN 1057  : DN10 ... DN108
//    - PVC ISO 1452   : DN20 ... DN315
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace e3gbimtools.Calc.Core.Hydraulics
{
    /// <summary>Una talla nominal de tubería con diámetro interior y exterior.</summary>
    public class NominalDiameter
    {
        /// <summary>Etiqueta normalizada: "DN15", "DN50", "108x1.5", ...</summary>
        public string Label { get; init; }

        /// <summary>Designación nominal numérica (mm).  Útil para ordenar.</summary>
        public int Dn { get; init; }

        /// <summary>Diámetro interior [m].  Es el que entra en Darcy-Weisbach.</summary>
        public double InnerDiameterM { get; init; }

        /// <summary>Diámetro exterior [m].</summary>
        public double OuterDiameterM { get; init; }

        /// <summary>Espesor de pared [m] = (Outer - Inner) / 2.</summary>
        public double WallThicknessM => (OuterDiameterM - InnerDiameterM) / 2.0;

        /// <summary>Sección interior [m²].</summary>
        public double AreaM2 => Math.PI * InnerDiameterM * InnerDiameterM / 4.0;

        /// <summary>Serie a la que pertenece ("EN10255_M", "Cu_EN1057", "PVC_ISO1452", ...).</summary>
        public string Series { get; init; }

        public override string ToString() => Label + " (Di=" + (InnerDiameterM * 1000.0).ToString("0.0") + " mm)";
    }

    /// <summary>
    /// Catálogos de tallas nominales y helpers para iterar / buscar la talla
    /// adecuada al dimensionar.  Las tallas están ordenadas de menor a mayor.
    /// </summary>
    public static class NominalDiameters
    {
        // -- Acero EN 10255 (medium series) ---------------------------------
        public static IReadOnlyList<NominalDiameter> SteelEN10255 { get; } = new[]
        {
            New("DN15",  15,  16.0e-3,  21.3e-3, "EN10255_M"),
            New("DN20",  20,  21.6e-3,  26.9e-3, "EN10255_M"),
            New("DN25",  25,  27.3e-3,  33.7e-3, "EN10255_M"),
            New("DN32",  32,  36.0e-3,  42.4e-3, "EN10255_M"),
            New("DN40",  40,  41.9e-3,  48.3e-3, "EN10255_M"),
            New("DN50",  50,  53.1e-3,  60.3e-3, "EN10255_M"),
            New("DN65",  65,  68.9e-3,  76.1e-3, "EN10255_M"),
            New("DN80",  80,  80.9e-3,  88.9e-3, "EN10255_M"),
            New("DN100", 100, 105.3e-3, 114.3e-3,"EN10255_M"),
            New("DN125", 125, 130.6e-3, 139.7e-3,"EN10255_M"),
            New("DN150", 150, 155.5e-3, 165.1e-3,"EN10255_M"),
            New("DN200", 200, 207.3e-3, 219.1e-3,"EN10255_M"),
            New("DN250", 250, 260.4e-3, 273.0e-3,"EN10255_M"),
            New("DN300", 300, 312.7e-3, 323.9e-3,"EN10255_M"),
        };

        // -- Cobre EN 1057 -------------------------------------------------
        public static IReadOnlyList<NominalDiameter> CopperEN1057 { get; } = new[]
        {
            New("Cu 10x1",   10,   8.0e-3,  10.0e-3, "Cu_EN1057"),
            New("Cu 12x1",   12,  10.0e-3,  12.0e-3, "Cu_EN1057"),
            New("Cu 15x1",   15,  13.0e-3,  15.0e-3, "Cu_EN1057"),
            New("Cu 18x1",   18,  16.0e-3,  18.0e-3, "Cu_EN1057"),
            New("Cu 22x1",   22,  20.0e-3,  22.0e-3, "Cu_EN1057"),
            New("Cu 28x1.5", 28,  25.0e-3,  28.0e-3, "Cu_EN1057"),
            New("Cu 35x1.5", 35,  32.0e-3,  35.0e-3, "Cu_EN1057"),
            New("Cu 42x1.5", 42,  39.0e-3,  42.0e-3, "Cu_EN1057"),
            New("Cu 54x2",   54,  50.0e-3,  54.0e-3, "Cu_EN1057"),
            New("Cu 64x2",   64,  60.0e-3,  64.0e-3, "Cu_EN1057"),
            New("Cu 76x2",   76,  72.0e-3,  76.0e-3, "Cu_EN1057"),
            New("Cu 89x2",   89,  85.0e-3,  89.0e-3, "Cu_EN1057"),
            New("Cu 108x2.5",108, 103.0e-3, 108.0e-3,"Cu_EN1057"),
        };

        // -- PVC ISO 1452 (PN10) -------------------------------------------
        public static IReadOnlyList<NominalDiameter> PvcISO1452 { get; } = new[]
        {
            New("PVC 20",  20,   16.6e-3,  20.0e-3, "PVC_ISO1452"),
            New("PVC 25",  25,   20.4e-3,  25.0e-3, "PVC_ISO1452"),
            New("PVC 32",  32,   26.2e-3,  32.0e-3, "PVC_ISO1452"),
            New("PVC 40",  40,   32.6e-3,  40.0e-3, "PVC_ISO1452"),
            New("PVC 50",  50,   40.8e-3,  50.0e-3, "PVC_ISO1452"),
            New("PVC 63",  63,   51.4e-3,  63.0e-3, "PVC_ISO1452"),
            New("PVC 75",  75,   61.4e-3,  75.0e-3, "PVC_ISO1452"),
            New("PVC 90",  90,   73.6e-3,  90.0e-3, "PVC_ISO1452"),
            New("PVC 110", 110,  90.0e-3,  110.0e-3,"PVC_ISO1452"),
            New("PVC 125", 125, 102.2e-3,  125.0e-3,"PVC_ISO1452"),
            New("PVC 160", 160, 130.8e-3,  160.0e-3,"PVC_ISO1452"),
            New("PVC 200", 200, 163.6e-3,  200.0e-3,"PVC_ISO1452"),
            New("PVC 250", 250, 204.6e-3,  250.0e-3,"PVC_ISO1452"),
            New("PVC 315", 315, 257.8e-3,  315.0e-3,"PVC_ISO1452"),
        };

        /// <summary>
        /// Serie por defecto que recorre el algoritmo de dimensionado del
        /// HydraulicCalculator (acero EN 10255).  El llamante puede pasar otra
        /// serie explícitamente.
        /// </summary>
        public static IReadOnlyList<NominalDiameter> StandardSeries => SteelEN10255;

        /// <summary>
        /// Devuelve la serie que mejor se ajusta al material.  Si no encuentra
        /// match específico devuelve <see cref="StandardSeries"/>.
        /// </summary>
        public static IReadOnlyList<NominalDiameter> SeriesForMaterial(string materialKey)
        {
            if (string.IsNullOrEmpty(materialKey)) return StandardSeries;
            string m = materialKey.ToUpperInvariant();
            if (m == "COPPER")                    return CopperEN1057;
            if (m == "PVC" || m == "PE" || m == "PP" || m == "PEX") return PvcISO1452;
            return SteelEN10255;
        }

        /// <summary>Busca una talla por etiqueta exacta (case-insensitive).</summary>
        public static NominalDiameter Find(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            return SteelEN10255.Concat(CopperEN1057).Concat(PvcISO1452)
                .FirstOrDefault(n => string.Equals(n.Label, label, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Devuelve la talla más pequeña de la serie cuyo Di sea >= a un
        /// diámetro objetivo.  Útil cuando ya conoces el diámetro mínimo
        /// requerido y quieres redondear a la talla comercial inmediata.
        /// </summary>
        public static NominalDiameter NextUpForInner(double targetInnerM, IReadOnlyList<NominalDiameter> series = null)
        {
            var s = series ?? StandardSeries;
            foreach (var nd in s)
                if (nd.InnerDiameterM >= targetInnerM) return nd;
            return s.Count > 0 ? s[s.Count - 1] : null;
        }

        private static NominalDiameter New(string label, int dn, double diM, double doM, string series)
            => new NominalDiameter
            {
                Label          = label,
                Dn             = dn,
                InnerDiameterM = diM,
                OuterDiameterM = doM,
                Series         = series,
            };
    }
}
