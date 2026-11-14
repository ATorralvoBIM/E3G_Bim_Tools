// ============================================================================
//  DuctSegment.cs
//  Core.Aeraulics — Tramo de conducto rectangular o circular.
//
//  Briefing técnico v1.0 — Mayo 2026, secciones 4.1 y 4.3.
//  Geometria circular (DiameterM > 0) o rectangular (WidthM, HeightM).
//  Propiedades derivadas:
//      D_h  = 2 W H / (W + H)                (diametro hidraulico)
//      D_eq = 1.30 (W H)^0.625 / (W + H)^0.25  (diametro equivalente Huebscher)
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;

namespace e3gbimtools.Calc.Core.Aeraulics
{
    /// <summary>Tipo de conducto.</summary>
    public enum DuctShape
    {
        Circular,
        Rectangular,
        Oval        // Aplastado / oval flat — futuro
    }

    /// <summary>
    /// Tramo de conducto entre dos nudos.  Almacena geometría, propiedades
    /// del material, caudal y resultados de cálculo aeráulico.  Unidades SI.
    /// </summary>
    public class DuctSegment
    {
        /// <summary>Id del elemento Revit asociado (Element.Id.Value).</summary>
        public long RevitId { get; set; }

        /// <summary>Forma del conducto.</summary>
        public DuctShape Shape { get; set; } = DuctShape.Circular;

        /// <summary>Diámetro interior si Shape == Circular [m].</summary>
        public double DiameterM { get; set; }

        /// <summary>Anchura interior si Shape == Rectangular [m].</summary>
        public double WidthM { get; set; }

        /// <summary>Altura interior si Shape == Rectangular [m].</summary>
        public double HeightM { get; set; }

        /// <summary>Longitud del tramo [m].</summary>
        public double LengthM { get; set; }

        /// <summary>Indice BFS desde la fuente (1 = tramo mas cercano a la fuente).</summary>
        public int BfsIndex { get; set; }

        /// <summary>Id de nudo aguas-arriba (BFS).</summary>
        public int FromNodeId { get; set; }

        /// <summary>Id de nudo aguas-abajo (BFS).</summary>
        public int ToNodeId { get; set; }

        /// <summary>Profundidad BFS desde la fuente.</summary>
        public int Depth { get; set; }

        /// <summary>True si el segmento desemboca en un nudo terminal.</summary>
        public bool IsTerminalSegment { get; set; }

        /// <summary>True si el segmento sale del nudo fuente.</summary>
        public bool IsSourceSegment { get; set; }

        /// <summary>Numero de hijos directos del nodo final (si > 1 es punto de bifurcacion).</summary>
        public int DownstreamBranchCount { get; set; }

        /// <summary>Nombre del nivel Revit del tramo.</summary>
        public string LevelName { get; set; }

        /// <summary>Caudal volumétrico [m³/s].</summary>
        public double FlowM3s { get; set; }

        /// <summary>Velocidad media calculada [m/s] = Q / A.</summary>
        public double VelocityMs { get; set; }

        /// <summary>Pérdida de carga lineal [Pa/m].</summary>
        public double FrictionPaPm { get; set; }

        /// <summary>Pérdida total del tramo [Pa] (lineal + singulares).</summary>
        public double TotalLossPa { get; set; }

        /// <summary>Rugosidad absoluta [m].  Chapa galvanizada estándar = 0.0001 m.</summary>
        public double Roughness { get; set; } = 0.0001;

        /// <summary>Tipo de servicio (Supply / Return / Exhaust / OutsideAir).</summary>
        public string SystemType { get; set; }

        /// <summary>Nudo origen del tramo.</summary>
        public DuctNode NodeStart { get; set; }

        /// <summary>Nudo destino del tramo.</summary>
        public DuctNode NodeEnd { get; set; }

        /// <summary>Accesorios singulares (codos, tés, transiciones, atenuadores).</summary>
        public List<DuctFittingLoss> Fittings { get; } = new List<DuctFittingLoss>();

        // -------------------------------------------------------------------
        //  Propiedades derivadas
        // -------------------------------------------------------------------

        /// <summary>Sección transversal interior [m²].</summary>
        public double AreaM2
        {
            get
            {
                if (Shape == DuctShape.Circular) return Math.PI * DiameterM * DiameterM / 4.0;
                if (Shape == DuctShape.Rectangular) return WidthM * HeightM;
                return WidthM * HeightM;
            }
        }

        /// <summary>
        /// Diámetro hidráulico = 4·A / P_mojado.
        /// Para circular: D.  Para rectangular: 2·W·H / (W+H).
        /// </summary>
        public double HydraulicDiameterM
        {
            get
            {
                if (Shape == DuctShape.Circular) return DiameterM;
                if (WidthM <= 0 || HeightM <= 0) return 0.0;
                return 2.0 * WidthM * HeightM / (WidthM + HeightM);
            }
        }

        /// <summary>
        /// Diámetro circular equivalente (Huebscher 1948).  Solo aplica a
        /// secciones rectangulares con relacion de aspecto W/H ≤ 4:1.
        /// D_eq = 1.30 (W H)^0.625 / (W + H)^0.25.
        /// </summary>
        public double EquivalentDiameterM
        {
            get
            {
                if (Shape == DuctShape.Circular) return DiameterM;
                if (WidthM <= 0 || HeightM <= 0) return 0.0;
                return 1.30 * Math.Pow(WidthM * HeightM, 0.625) / Math.Pow(WidthM + HeightM, 0.25);
            }
        }

        /// <summary>Pérdida lineal del tramo (sin singulares) [Pa].</summary>
        public double LinearLossPa => FrictionPaPm * LengthM;

        /// <summary>Suma de pérdidas singulares del tramo [Pa].</summary>
        public double SingularLossPa => TotalLossPa - LinearLossPa;

        /// <summary>Etiqueta corta de la talla: "250Ø" o "400x200".</summary>
        public string SizeLabel
        {
            get
            {
                if (Shape == DuctShape.Circular)
                    return (DiameterM * 1000.0).ToString("0") + "Ø";
                return (WidthM * 1000.0).ToString("0") + "x" + (HeightM * 1000.0).ToString("0");
            }
        }

        public override string ToString()
        {
            string s = NodeStart == null ? "?" : ("N" + NodeStart.Id);
            string e = NodeEnd   == null ? "?" : ("N" + NodeEnd.Id);
            return "Duct[" + s + "->" + e + "] " + SizeLabel
                 + " L=" + LengthM.ToString("0.00") + " m"
                 + " Q=" + (FlowM3s * 1000.0).ToString("0") + " L/s"
                 + " v=" + VelocityMs.ToString("0.00") + " m/s";
        }
    }
}
