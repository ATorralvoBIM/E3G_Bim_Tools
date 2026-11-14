// ============================================================================
//  DuctSizingSeries.cs
//  Core.Aeraulics — Series de tallas estándar de conductos.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 4.2.
//  Series cubiertas:
//    - Circular EN 1506 (chapa galvanizada espiroidal): 100, 125, 160, 200,
//      250, 315, 400, 500, 630, 800, 1000, 1250 mm.
//    - Rectangular SMACNA: combinaciones W x H comunes en HVAC con W/H ≤ 4:1.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace e3gbimtools.Calc.Core.Aeraulics
{
    /// <summary>Talla circular nominal de un conducto.</summary>
    public class CircularDuctSize
    {
        public string Label { get; init; }
        public int    Dn    { get; init; }
        public double DiameterM { get; init; }
        public double AreaM2 => Math.PI * DiameterM * DiameterM / 4.0;
        public override string ToString() => Label;
    }

    /// <summary>Talla rectangular nominal de un conducto.</summary>
    public class RectangularDuctSize
    {
        public string Label  { get; init; }
        public double WidthM { get; init; }
        public double HeightM{ get; init; }
        public double AreaM2 => WidthM * HeightM;
        public double HydraulicDiameterM
            => 2.0 * WidthM * HeightM / (WidthM + HeightM);
        public double EquivalentDiameterM
            => 1.30 * Math.Pow(WidthM * HeightM, 0.625) / Math.Pow(WidthM + HeightM, 0.25);
        public double AspectRatio => Math.Max(WidthM, HeightM) / Math.Min(WidthM, HeightM);
        public override string ToString() => Label;
    }

    /// <summary>
    /// Catálogos de tallas estándar de conductos.  Las tallas están ordenadas
    /// de menor a mayor sección.
    /// </summary>
    public static class DuctSizingSeries
    {
        // -- Circular EN 1506 (mm) -----------------------------------------
        private static readonly int[] CircularDnMm =
            { 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250 };

        public static IReadOnlyList<CircularDuctSize> Circular { get; } =
            CircularDnMm.Select(dn => new CircularDuctSize
            {
                Label     = dn.ToString() + " O",
                Dn        = dn,
                DiameterM = dn / 1000.0,
            }).ToList();

        // -- Rectangular SMACNA (W x H mm) ---------------------------------
        // Selección representativa con W/H entre 1:1 y 4:1, paso 50 mm.
        // No es exhaustiva; cubre los tamaños mas comunes en edificios.
        private static readonly (int w, int h)[] RectMm = new[]
        {
            (200, 100), (200, 150), (200, 200),
            (250, 150), (250, 200), (250, 250),
            (300, 150), (300, 200), (300, 250), (300, 300),
            (400, 150), (400, 200), (400, 250), (400, 300), (400, 400),
            (500, 200), (500, 250), (500, 300), (500, 400), (500, 500),
            (600, 200), (600, 250), (600, 300), (600, 400), (600, 500), (600, 600),
            (800, 250), (800, 300), (800, 400), (800, 500), (800, 600), (800, 800),
            (1000, 300), (1000, 400), (1000, 500), (1000, 600), (1000, 800), (1000, 1000),
            (1200, 400), (1200, 500), (1200, 600), (1200, 800), (1200, 1000),
        };

        public static IReadOnlyList<RectangularDuctSize> Rectangular { get; } =
            RectMm
                .Select(p => new RectangularDuctSize
                {
                    Label   = p.w.ToString() + "x" + p.h.ToString(),
                    WidthM  = p.w / 1000.0,
                    HeightM = p.h / 1000.0,
                })
                .OrderBy(s => s.AreaM2)
                .ToList();

        // -- Helpers --------------------------------------------------------

        /// <summary>Busca una talla circular por DN exacto (mm).</summary>
        public static CircularDuctSize FindCircular(int dnMm)
            => Circular.FirstOrDefault(c => c.Dn == dnMm);

        /// <summary>
        /// Devuelve la talla circular más pequeña con D >= targetM.  Si no
        /// hay suficiente devuelve la mayor de la serie.
        /// </summary>
        public static CircularDuctSize NextUpCircular(double targetDiameterM)
        {
            foreach (var c in Circular)
                if (c.DiameterM >= targetDiameterM) return c;
            return Circular.Count > 0 ? Circular[Circular.Count - 1] : null;
        }

        /// <summary>
        /// Devuelve la talla rectangular más pequeña con area >= targetAreaM2.
        /// Itera por orden creciente de superficie.
        /// </summary>
        public static RectangularDuctSize NextUpRectangular(double targetAreaM2)
        {
            foreach (var r in Rectangular)
                if (r.AreaM2 >= targetAreaM2) return r;
            return Rectangular.Count > 0 ? Rectangular[Rectangular.Count - 1] : null;
        }
    }
}
