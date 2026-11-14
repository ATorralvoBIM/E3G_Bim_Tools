// ============================================================================
//  DuctNetwork.cs
//  Core.Aeraulics — Contenedor de la red de conductos.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 4.
//  Análogo a Core.Hydraulics.PipeNetwork.
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using e3gbimtools.Calc.Core.Common;

namespace e3gbimtools.Calc.Core.Aeraulics
{
    /// <summary>
    /// Red aeráulica de conductos.  Construida típicamente por
    /// DuctNetworkReader (RevitBridge) o por código de prueba.
    /// </summary>
    public class DuctNetwork
    {
        public List<DuctNode> Nodes { get; } = new List<DuctNode>();
        public List<DuctSegment> Segments { get; } = new List<DuctSegment>();
        public DuctNode SourceNode { get; set; }
        public List<DuctNode> TerminalNodes { get; } = new List<DuctNode>();

        /// <summary>Presión total disponible en SourceNode [Pa].</summary>
        public double SourcePressurePa { get; set; }

        /// <summary>Etiqueta del sistema MEP origen ("Supply Air", ...).</summary>
        public string SystemName { get; set; }

        public DuctNode AddNode(DuctNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (!Nodes.Contains(node)) Nodes.Add(node);
            if (node.IsSource && SourceNode == null) SourceNode = node;
            if (node.IsTerminal && !TerminalNodes.Contains(node)) TerminalNodes.Add(node);
            return node;
        }

        /// <summary>
        /// Añade un tramo y sus nodos extremos, asignando un id sintético
        /// negativo si el RevitId estaba a 0 (mismo patrón que PipeNetwork).
        /// </summary>
        public DuctSegment AddSegment(DuctSegment segment)
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

        public DuctNode CreateNode(double elevationM = 0.0, string label = null)
        {
            int id = (Nodes.Count == 0) ? 1 : Nodes.Max(n => n.Id) + 1;
            var n = new DuctNode { Id = id, ElevationM = elevationM, Label = label };
            return AddNode(n);
        }

        /// <summary>Helper: crea un tramo CIRCULAR entre dos nudos.</summary>
        public DuctSegment ConnectCircular(DuctNode start, DuctNode end,
            double diameterM, double lengthM, string systemType = null)
        {
            var seg = new DuctSegment
            {
                NodeStart  = start,
                NodeEnd    = end,
                Shape      = DuctShape.Circular,
                DiameterM  = diameterM,
                LengthM    = lengthM,
                SystemType = systemType,
            };
            return AddSegment(seg);
        }

        /// <summary>Helper: crea un tramo RECTANGULAR entre dos nudos.</summary>
        public DuctSegment ConnectRectangular(DuctNode start, DuctNode end,
            double widthM, double heightM, double lengthM, string systemType = null)
        {
            var seg = new DuctSegment
            {
                NodeStart  = start,
                NodeEnd    = end,
                Shape      = DuctShape.Rectangular,
                WidthM     = widthM,
                HeightM    = heightM,
                LengthM    = lengthM,
                SystemType = systemType,
            };
            return AddSegment(seg);
        }

        /// <summary>NetworkGraph que apunta a las mismas instancias.</summary>
        public NetworkGraph<DuctNode, DuctSegment> ToGraph()
        {
            var g = new NetworkGraph<DuctNode, DuctSegment>
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

        public bool IsTree() => !ToGraph().HasCycle();

        /// <summary>
        /// Orienta todos los segmentos partiendo de SourceNode: tras la
        /// llamada, NodeStart de cada segmento queda siempre del lado fuente
        /// y NodeEnd del lado terminal.  Necesario cuando la red se construye
        /// leyendo Revit (donde el orden de connectors es arbitrario) antes
        /// de llamar al AeraulicCalculator.
        /// </summary>
        public void OrientFromSource()
        {
            if (SourceNode == null) return;
            var visited = new HashSet<DuctNode> { SourceNode };
            var queue = new Queue<DuctNode>();
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

        public string Summary()
        {
            double totalLen = 0.0;
            for (int i = 0; i < Segments.Count; i++) totalLen += Segments[i].LengthM;
            return "Red '" + (SystemName ?? "(sin nombre)") + "': "
                 + Nodes.Count + " nudos, " + Segments.Count + " tramos, "
                 + TerminalNodes.Count + " terminales, "
                 + totalLen.ToString("0.0") + " m de conducto.";
        }
    }
}
