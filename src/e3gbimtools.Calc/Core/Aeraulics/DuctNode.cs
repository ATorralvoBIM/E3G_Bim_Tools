// ============================================================================
//  DuctNode.cs
//  Core.Aeraulics — Nodo (nudo) de una red aeráulica de conductos.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 4.
//  Análogo a Core.Hydraulics.PipeNode pero para aire: presiones total y
//  estatica, demanda volumetrica.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System.Collections.Generic;

namespace e3gbimtools.Calc.Core.Aeraulics
{
    /// <summary>
    /// Nudo de la red de conductos.  Representa un punto donde convergen
    /// tramos (intersección, terminal de impulsión/extracción, UTA, ventilador).
    /// </summary>
    public class DuctNode
    {
        /// <summary>Identificador interno del nodo dentro de la red.</summary>
        public int Id { get; set; }

        /// <summary>Etiqueta humana opcional ("Difusor-1", "UTA-PB", ...).</summary>
        public string Label { get; set; }

        /// <summary>Presión total calculada en el nudo [Pa].</summary>
        public double TotalPressurePa { get; set; }

        /// <summary>Presión estática calculada en el nudo [Pa].</summary>
        public double StaticPressurePa { get; set; }

        /// <summary>Cota del nudo respecto al origen [m].</summary>
        public double ElevationM { get; set; }

        /// <summary>Coordenada X del nudo [m] (origen del connector). Para localizar cortes.</summary>
        public double X { get; set; }

        /// <summary>Coordenada Y del nudo [m] (origen del connector). Para localizar cortes.</summary>
        public double Y { get; set; }

        /// <summary>True si es la fuente de aire (ventilador / UTA / impulsión).</summary>
        public bool IsSource { get; set; }

        /// <summary>True si es un terminal (rejilla, difusor, extracción).</summary>
        public bool IsTerminal { get; set; }

        /// <summary>Caudal demandado en el nudo (solo terminales) [m³/s].</summary>
        public double DemandM3s { get; set; }

        /// <summary>
        /// Perdida de carga del terminal de aire (rejilla, difusor) en este
        /// nudo [Pa].  Se suma al WorstCasePressureDropPa pero no afecta a
        /// la presion local del nudo (representa el salto a la sala).
        /// </summary>
        public double TerminalPressureDropPa { get; set; }

        /// <summary>
        /// Tramos físicamente conectados al nodo (entrantes + salientes).
        /// </summary>
        public List<DuctSegment> ConnectedSegments { get; } = new List<DuctSegment>();

        public override string ToString()
        {
            string tag = string.IsNullOrEmpty(Label) ? ("N" + Id) : Label;
            return tag + " (Pt=" + TotalPressurePa.ToString("0") + " Pa, z="
                 + ElevationM.ToString("0.00") + " m)";
        }
    }
}
