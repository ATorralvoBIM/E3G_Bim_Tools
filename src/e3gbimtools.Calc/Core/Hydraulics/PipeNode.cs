// ============================================================================
//  PipeNode.cs
//  Core.Hydraulics — Nodo (nudo) de una red hidráulica de tuberías.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.2.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System.Collections.Generic;

namespace e3gbimtools.Calc.Core.Hydraulics
{
    /// <summary>
    /// Nudo de la red hidráulica.  Representa un punto donde convergen tramos
    /// (intersección, terminal de consumo, fuente de presión, expansión).
    /// </summary>
    public class PipeNode
    {
        /// <summary>Identificador interno del nodo dentro de la red.</summary>
        public int Id { get; set; }

        /// <summary>
        /// Etiqueta humana opcional ("N1", "WC-Planta-2", "Bomba"...).  Solo
        /// para informes y depuración; el algoritmo no la usa.
        /// </summary>
        public string Label { get; set; }

        /// <summary>Presión absoluta calculada en el nudo [Pa].</summary>
        public double PressurePa { get; set; }

        /// <summary>Cota del nudo respecto al origen del proyecto [m]. Para presión estática.</summary>
        public double ElevationM { get; set; }

        /// <summary>Coordenada X del nudo [m] (origen del connector). Para localizar cortes.</summary>
        public double X { get; set; }

        /// <summary>Coordenada Y del nudo [m] (origen del connector). Para localizar cortes.</summary>
        public double Y { get; set; }

        /// <summary>True si es la fuente de la red (bomba, depósito, acometida).</summary>
        public bool IsSource { get; set; }

        /// <summary>True si es un terminal de consumo (grifo, radiador, difusor).</summary>
        public bool IsTerminal { get; set; }

        /// <summary>Caudal demandado en el nudo (solo terminales) [m³/s].</summary>
        public double DemandM3s { get; set; }

        /// <summary>
        /// Tramos físicamente conectados al nodo (entrantes + salientes).
        /// Lo rellena el constructor de la red al añadir cada tramo.
        /// </summary>
        public List<PipeSegment> ConnectedSegments { get; } = new List<PipeSegment>();

        public override string ToString()
        {
            string tag = string.IsNullOrEmpty(Label) ? ("N" + Id) : Label;
            return tag + " (P=" + PressurePa.ToString("0") + " Pa, z=" + ElevationM.ToString("0.00") + " m)";
        }
    }
}
