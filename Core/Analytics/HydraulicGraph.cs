// ============================================================================
//  HydraulicGraph.cs
//  Tipos de dominio para razonar sobre la red ya calculada como un GRAFO
//  dirigido: el agua va de la fuente hacia los terminales.  Estos tipos son
//  ligeros (records inmutables); se reconstruyen a partir de la lista de
//  ResultRow tras cada cálculo.  La capa de UI/Revit no necesita conocerlos
//  — los usan los servicios analíticos (CriticalPathService, HeatmapService,
//  futuras optimizaciones, etc.).
// ============================================================================
using System.Collections.Generic;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.Core.Analytics
{
    /// <summary>
    /// Nodo del grafo hidráulico — equivale a un nudo físico de la red:
    /// la fuente, un fitting (TEE, ELBOW, etc.) o un terminal de consumo.
    /// </summary>
    public sealed class HydraulicNode
    {
        public string Id { get; }
        public ResultRow IncomingPipe { get; }
        public List<ResultRow> OutgoingPipes { get; } = new List<ResultRow>();

        /// <summary>
        /// ΔP acumulado en este nudo, medido desde la fuente hasta aquí.
        /// Se rellena durante el análisis (CriticalPathService).
        /// </summary>
        public double AccumulatedPressureDropPa { get; set; }

        /// <summary>True si este nodo es un consumidor terminal de la red.</summary>
        public bool IsTerminal { get; set; }

        public HydraulicNode(string id, ResultRow incomingPipe)
        {
            Id = id;
            IncomingPipe = incomingPipe;
        }

        public override string ToString()
            => $"Node {Id} (in: {IncomingPipe?.NodePath ?? "src"}, out: {OutgoingPipes.Count})";
    }

    /// <summary>
    /// Arista del grafo — equivale a un tramo de tubería/conducto.  La
    /// arista lleva los datos hidráulicos (Q, v, ΔP, DN) directamente en
    /// la ResultRow asociada para no duplicar información.
    /// </summary>
    public sealed class HydraulicEdge
    {
        public ResultRow Pipe { get; }
        public HydraulicNode From { get; }
        public HydraulicNode To { get; }

        public HydraulicEdge(ResultRow pipe, HydraulicNode from, HydraulicNode to)
        {
            Pipe = pipe;
            From = from;
            To = to;
        }

        public override string ToString()
            => $"Edge {Pipe?.NodePath ?? "?"} ({From?.Id} → {To?.Id})";
    }
}
