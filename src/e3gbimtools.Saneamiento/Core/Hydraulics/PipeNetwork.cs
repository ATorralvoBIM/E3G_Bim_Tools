// ============================================================================
//  PipeNetwork.cs
//  Core.Hydraulics — Contenedor de la red completa de tuberías a presión.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.2.
//  Mantiene listas de PipeNode y PipeSegment, la fuente de la red, los
//  terminales y la presión disponible en la fuente.  Expone un NetworkGraph
//  para reutilizar BFS / topo-sort / detección de ciclos.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using E3G.Saneamiento.Core.Common;

namespace E3G.Saneamiento.Core.Hydraulics
{
    /// <summary>
    /// Red hidráulica de tuberías.  Construida típicamente por
    /// PipeNetworkReader (RevitBridge) o por código de prueba.
    /// </summary>
    public class PipeNetwork
    {
        /// <summary>Nudos de la red.</summary>
        public List<PipeNode> Nodes { get; } = new List<PipeNode>();

        /// <summary>Tramos de la red.</summary>
        public List<PipeSegment> Segments { get; } = new List<PipeSegment>();

        /// <summary>Nodo origen (bomba, depósito, acometida).</summary>
        public PipeNode SourceNode { get; set; }

        /// <summary>Nodos terminales (puntos de consumo).</summary>
        public List<PipeNode> TerminalNodes { get; } = new List<PipeNode>();

        /// <summary>Presión disponible en SourceNode [Pa].</summary>
        public double SourcePressurePa { get; set; }

        /// <summary>Etiqueta del sistema MEP origen ("Domestic Cold Water", ...).</summary>
        public string SystemName { get; set; }

        // -------------------------------------------------------------------
        //  Mutación
        // -------------------------------------------------------------------

        /// <summary>Añade un nodo si no estaba.</summary>
        public PipeNode AddNode(PipeNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (!Nodes.Contains(node)) Nodes.Add(node);
            if (node.IsSource && SourceNode == null) SourceNode = node;
            if (node.IsTerminal && !TerminalNodes.Contains(node)) TerminalNodes.Add(node);
            return node;
        }

        /// <summary>
        /// Añade un tramo y sus nodos extremos.  Mantiene la lista
        /// ConnectedSegments coherente.  Si el tramo no trae RevitId
        /// (creación programatica) se le asigna un id sintetico negativo
        /// y unico para que CalcReport.Segments (Dictionary indexado por
        /// RevitId) no sufra colisiones.
        /// </summary>
        public PipeSegment AddSegment(PipeSegment segment)
        {
            if (segment == null) throw new ArgumentNullException(nameof(segment));
            if (segment.NodeStart != null) AddNode(segment.NodeStart);
            if (segment.NodeEnd   != null) AddNode(segment.NodeEnd);
            if (segment.RevitId == 0L) segment.RevitId = -(Segments.Count + 1);
            Segments.Add(segment);
            if (segment.NodeStart != null && !segment.NodeStart.ConnectedSegments.Contains(segment))
                segment.NodeStart.ConnectedSegments.Add(segment);
            if (segment.NodeEnd != null && !segment.NodeEnd.ConnectedSegments.Contains(segment))
                segment.NodeEnd.ConnectedSegments.Add(segment);
            return segment;
        }

        /// <summary>Crea un nodo nuevo con Id auto-asignado y lo añade.</summary>
        public PipeNode CreateNode(double elevationM = 0.0, string label = null)
        {
            int id = (Nodes.Count == 0) ? 1 : Nodes.Max(n => n.Id) + 1;
            var n = new PipeNode { Id = id, ElevationM = elevationM, Label = label };
            return AddNode(n);
        }

        /// <summary>
        /// Helper de construcción para tests/uso programático: crea un tramo
        /// dadas dos referencias a nudos existentes y la geometría.
        /// </summary>
        public PipeSegment ConnectNodes(PipeNode start, PipeNode end,
            double diameterM, double lengthM, string materialKey)
        {
            var seg = new PipeSegment
            {
                NodeStart   = start,
                NodeEnd     = end,
                DiameterM   = diameterM,
                LengthM     = lengthM,
                MaterialKey = materialKey,
                Roughness   = MaterialDatabase.GetRoughness(materialKey),
            };
            return AddSegment(seg);
        }

        // -------------------------------------------------------------------
        //  Vista de grafo (reutiliza algoritmos de Core.Common)
        // -------------------------------------------------------------------

        /// <summary>
        /// Devuelve un NetworkGraph que apunta a las mismas instancias de
        /// nudos y tramos.  Útil para BFS, topological sort, detección de
        /// ciclos y otras operaciones genéricas.
        /// </summary>
        public NetworkGraph<PipeNode, PipeSegment> ToGraph()
        {
            var g = new NetworkGraph<PipeNode, PipeSegment>
            {
                GetEdgeStart = e => e.NodeStart,
                GetEdgeEnd   = e => e.NodeEnd,
                GetNodeEdges = n => n.ConnectedSegments,
                SourceNode   = SourceNode,
            };
            foreach (var n in Nodes) g.AddNode(n);
            foreach (var s in Segments) g.Edges.Add(s);
            foreach (var t in TerminalNodes) g.TerminalNodes.Add(t);
            return g;
        }

        /// <summary>True si la red es un árbol orientado (sin ciclos dirigidos).</summary>
        public bool IsTree() => !ToGraph().HasCycle();

        /// <summary>
        /// Orienta todos los segmentos partiendo de SourceNode: tras la
        /// llamada, NodeStart de cada segmento queda siempre del lado fuente
        /// y NodeEnd del lado terminal.  Necesario cuando la red se construye
        /// leyendo Revit (donde el orden de connectors es arbitrario) antes
        /// de llamar al HydraulicCalculator.
        /// </summary>
        public void OrientFromSource()
        {
            if (SourceNode == null) return;
            var visited = new HashSet<PipeNode> { SourceNode };
            var queue = new Queue<PipeNode>();
            queue.Enqueue(SourceNode);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var seg in node.ConnectedSegments)
                {
                    var other = ReferenceEquals(seg.NodeStart, node)
                        ? seg.NodeEnd : seg.NodeStart;
                    if (other == null || visited.Contains(other)) continue;
                    if (!ReferenceEquals(seg.NodeStart, node))
                    {
                        var tmp = seg.NodeStart;
                        seg.NodeStart = seg.NodeEnd;
                        seg.NodeEnd = tmp;
                    }
                    visited.Add(other);
                    queue.Enqueue(other);
                }
            }
        }

        /// <summary>
        /// Resumen para informes: número de nudos, tramos, terminales y
        /// longitud total de tubería.
        /// </summary>
        public string Summary()
        {
            double totalLen = 0.0;
            for (int i = 0; i < Segments.Count; i++) totalLen += Segments[i].LengthM;
            return "Red '" + (SystemName ?? "(sin nombre)") + "': "
                 + Nodes.Count + " nudos, " + Segments.Count + " tramos, "
                 + TerminalNodes.Count + " terminales, "
                 + totalLen.ToString("0.0") + " m de tuberia.";
        }
    }
}
