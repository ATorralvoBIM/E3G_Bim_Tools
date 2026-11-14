// ============================================================================
//  PipeSegment.cs
//  Core.Hydraulics — Tramo (segment) de tubería.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.2.
//  Cambio respecto al briefing: RevitId pasa de int a long para alinear con la
//  API de Revit 2024+ (ElementId.Value es long).
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System.Collections.Generic;

namespace E3G.Saneamiento.Core.Hydraulics
{
    /// <summary>
    /// Tramo de tubería entre dos nudos.  Almacena geometría, propiedades del
    /// material, caudal y resultados de cálculo (velocidad, fricción, pérdida
    /// total, accesorios).  Las unidades son SI.
    /// </summary>
    public class PipeSegment
    {
        /// <summary>Id del elemento Revit al que corresponde el tramo (Element.Id.Value).</summary>
        public long RevitId { get; set; }

        /// <summary>Diámetro interior [m].</summary>
        public double DiameterM { get; set; }

        /// <summary>Longitud del tramo [m].</summary>
        public double LengthM { get; set; }

        /// <summary>Caudal volumétrico [m³/s].  Signo positivo en sentido NodeStart -> NodeEnd.
        /// Si hay simultaneidad aplicada, este valor es el caudal de DISEÑO (con K aplicado).</summary>
        public double FlowM3s { get; set; }

        /// <summary>Caudal volumétrico bruto sin simultaneidad [m³/s] = suma de demandas aguas-abajo.</summary>
        public double RawFlowM3s { get; set; }

        /// <summary>Numero de aparatos terminales con demanda > 0 aguas-abajo de este tramo.</summary>
        public int DownstreamFixtureCount { get; set; }

        /// <summary>Factor de simultaneidad aplicado al tramo (1.0 si sin simultaneidad).</summary>
        public double SimultaneityFactor { get; set; } = 1.0;

        /// <summary>Indice de orden BFS desde la fuente (1 = primer tramo de acometida).</summary>
        public int BfsIndex { get; set; }

        /// <summary>Id de nudo aguas-arriba (renumerado por BFS).</summary>
        public int FromNodeId { get; set; }

        /// <summary>Id de nudo aguas-abajo (renumerado por BFS).</summary>
        public int ToNodeId { get; set; }

        /// <summary>Profundidad BFS desde la fuente (0 = fuente, 1 = primer tramo, etc.).</summary>
        public int Depth { get; set; }

        /// <summary>True si el segmento desemboca en un nudo terminal de consumo.</summary>
        public bool IsTerminalSegment { get; set; }

        /// <summary>True si el segmento sale del nudo fuente (acometida).</summary>
        public bool IsSourceSegment { get; set; }

        /// <summary>Numero de hijos directos del nodo final (si > 1 es punto de bifurcacion).</summary>
        public int DownstreamBranchCount { get; set; }

        /// <summary>Nombre del nivel Revit asociado al tramo (ReferenceLevel).</summary>
        public string LevelName { get; set; }

        /// <summary>Velocidad media calculada [m/s] = Q / A.</summary>
        public double VelocityMs { get; set; }

        /// <summary>Pérdida de carga lineal [Pa/m] (Darcy-Weisbach).</summary>
        public double FrictionPaPm { get; set; }

        /// <summary>Pérdida total del tramo [Pa] = FrictionPaPm * LengthM + Σ pérdidas singulares.</summary>
        public double TotalLossPa { get; set; }

        /// <summary>Rugosidad absoluta del material [m].  Ver MaterialDatabase.</summary>
        public double Roughness { get; set; }

        /// <summary>Clave del material en MaterialDatabase ("STEEL_GALV", "COPPER", ...).</summary>
        public string MaterialKey { get; set; }

        /// <summary>Tipo de sistema MEP al que pertenece ("Domestic Cold Water", "Heating", ...).</summary>
        public string SystemType { get; set; }

        /// <summary>Nudo origen del tramo.</summary>
        public PipeNode NodeStart { get; set; }

        /// <summary>Nudo destino del tramo.</summary>
        public PipeNode NodeEnd { get; set; }

        /// <summary>Accesorios singulares incluidos en el tramo (codos, tés, válvulas).</summary>
        public List<FittingLoss> Fittings { get; } = new List<FittingLoss>();

        /// <summary>Sección transversal interior [m²].</summary>
        public double AreaM2 => System.Math.PI * DiameterM * DiameterM / 4.0;

        /// <summary>Pérdida lineal del tramo (sin singulares) [Pa].</summary>
        public double LinearLossPa => FrictionPaPm * LengthM;

        /// <summary>Suma de pérdidas singulares del tramo [Pa].</summary>
        public double SingularLossPa => TotalLossPa - LinearLossPa;

        public override string ToString()
        {
            string s = NodeStart == null ? "?" : ("N" + NodeStart.Id);
            string e = NodeEnd   == null ? "?" : ("N" + NodeEnd.Id);
            return "Pipe[" + s + "->" + e + "] DN " + (DiameterM * 1000.0).ToString("0")
                 + " L=" + LengthM.ToString("0.00") + " m"
                 + " Q=" + (FlowM3s * 1000.0).ToString("0.00") + " L/s"
                 + " v=" + VelocityMs.ToString("0.00") + " m/s";
        }
    }
}
