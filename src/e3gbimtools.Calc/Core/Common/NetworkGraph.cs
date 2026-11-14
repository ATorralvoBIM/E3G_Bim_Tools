// ============================================================================
//  NetworkGraph.cs
//  Core.Common — Grafo dirigido genérico para redes hidráulicas y aeráulicas.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.2 (firma) y sección 5.
//  Esta implementación amplía la firma del briefing con:
//    - Adyacencia funcional (configurable mediante delegados)
//    - BFS / DFS
//    - Topological sort sobre aristas y sobre nodos (Kahn)
//    - Detección de ciclos
//    - Búsqueda de raíces (sin entradas) y hojas (sin salidas)
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace e3gbimtools.Calc.Core.Common
{
    /// <summary>
    /// Grafo dirigido reutilizable.  TNode y TEdge son las entidades de dominio
    /// (PipeNode/PipeSegment, DuctNode/DuctSegment, etc.).  La conectividad se
    /// declara mediante delegados, evitando interfaces que obligaran a contaminar
    /// los objetos de dominio.
    /// </summary>
    public class NetworkGraph<TNode, TEdge>
        where TNode : class
        where TEdge : class
    {
        // --------------------------------------------------------------------
        //  Estructura básica (compatibilidad con la firma del briefing § 3.2)
        // --------------------------------------------------------------------

        /// <summary>Nodos del grafo.</summary>
        public List<TNode> Nodes { get; } = new();

        /// <summary>Aristas del grafo.</summary>
        public List<TEdge> Edges { get; } = new();

        /// <summary>Nodo de origen (bomba / UTA / fuente de presión).</summary>
        public TNode SourceNode { get; set; }

        /// <summary>Nodos terminales (consumos).</summary>
        public List<TNode> TerminalNodes { get; } = new();

        // --------------------------------------------------------------------
        //  Adaptadores funcionales: cómo se conecta una arista con sus nodos
        // --------------------------------------------------------------------

        /// <summary>Función que devuelve el nodo origen de una arista.</summary>
        public Func<TEdge, TNode> GetEdgeStart { get; set; }

        /// <summary>Función que devuelve el nodo destino de una arista.</summary>
        public Func<TEdge, TNode> GetEdgeEnd { get; set; }

        /// <summary>
        /// Función opcional que devuelve todas las aristas conectadas a un nodo
        /// (entrantes + salientes).  Si es null, se calcula a partir de Edges.
        /// </summary>
        public Func<TNode, IEnumerable<TEdge>> GetNodeEdges { get; set; }

        // --------------------------------------------------------------------
        //  API de mutación
        // --------------------------------------------------------------------

        /// <summary>Añade un nodo al grafo si no estaba.</summary>
        public void AddNode(TNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (!Nodes.Contains(node)) Nodes.Add(node);
        }

        /// <summary>Añade una arista al grafo.  Sus nodos extremos se añaden si faltan.</summary>
        public void AddEdge(TEdge edge)
        {
            if (edge == null) throw new ArgumentNullException(nameof(edge));
            EnsureAdapters();
            var s = GetEdgeStart(edge);
            var e = GetEdgeEnd(edge);
            if (s != null) AddNode(s);
            if (e != null) AddNode(e);
            Edges.Add(edge);
        }

        // --------------------------------------------------------------------
        //  Consultas topológicas
        // --------------------------------------------------------------------

        /// <summary>Aristas salientes de un nodo (NodeStart == n).</summary>
        public IEnumerable<TEdge> OutgoingEdges(TNode node)
        {
            EnsureAdapters();
            if (GetNodeEdges != null)
                return GetNodeEdges(node).Where(e => ReferenceEquals(GetEdgeStart(e), node));
            return Edges.Where(e => ReferenceEquals(GetEdgeStart(e), node));
        }

        /// <summary>Aristas entrantes a un nodo (NodeEnd == n).</summary>
        public IEnumerable<TEdge> IncomingEdges(TNode node)
        {
            EnsureAdapters();
            if (GetNodeEdges != null)
                return GetNodeEdges(node).Where(e => ReferenceEquals(GetEdgeEnd(e), node));
            return Edges.Where(e => ReferenceEquals(GetEdgeEnd(e), node));
        }

        /// <summary>Vecinos directos siguiendo aristas salientes.</summary>
        public IEnumerable<TNode> Successors(TNode node)
            => OutgoingEdges(node).Select(e => GetEdgeEnd(e)).Where(n => n != null);

        /// <summary>Vecinos directos siguiendo aristas entrantes.</summary>
        public IEnumerable<TNode> Predecessors(TNode node)
            => IncomingEdges(node).Select(e => GetEdgeStart(e)).Where(n => n != null);

        /// <summary>Grado entrante (número de aristas que llegan al nodo).</summary>
        public int InDegree(TNode node) => IncomingEdges(node).Count();

        /// <summary>Grado saliente (número de aristas que salen del nodo).</summary>
        public int OutDegree(TNode node) => OutgoingEdges(node).Count();

        /// <summary>Nodos sin aristas entrantes (raíces / fuentes).</summary>
        public IEnumerable<TNode> Roots() => Nodes.Where(n => InDegree(n) == 0);

        /// <summary>Nodos sin aristas salientes (hojas / terminales).</summary>
        public IEnumerable<TNode> Leaves() => Nodes.Where(n => OutDegree(n) == 0);

        // --------------------------------------------------------------------
        //  Recorridos
        // --------------------------------------------------------------------

        /// <summary>BFS partiendo de <paramref name="start"/> siguiendo aristas dirigidas.</summary>
        public IEnumerable<TNode> Bfs(TNode start)
        {
            if (start == null) yield break;
            var visited = new HashSet<TNode>(ReferenceComparer);
            var queue   = new Queue<TNode>();
            queue.Enqueue(start);
            visited.Add(start);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                yield return n;
                foreach (var s in Successors(n))
                {
                    if (visited.Add(s)) queue.Enqueue(s);
                }
            }
        }

        /// <summary>DFS partiendo de <paramref name="start"/> siguiendo aristas dirigidas.</summary>
        public IEnumerable<TNode> Dfs(TNode start)
        {
            if (start == null) yield break;
            var visited = new HashSet<TNode>(ReferenceComparer);
            var stack   = new Stack<TNode>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!visited.Add(n)) continue;
                yield return n;
                foreach (var s in Successors(n)) stack.Push(s);
            }
        }

        /// <summary>Devuelve las aristas en orden BFS desde la fuente (útil para propagar presiones).</summary>
        public IList<TEdge> BreadthFirstOrderEdges()
        {
            var result = new List<TEdge>();
            if (SourceNode == null) return result;
            var visited = new HashSet<TNode>(ReferenceComparer) { SourceNode };
            var queue   = new Queue<TNode>();
            queue.Enqueue(SourceNode);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                foreach (var edge in OutgoingEdges(n))
                {
                    result.Add(edge);
                    var next = GetEdgeEnd(edge);
                    if (next != null && visited.Add(next))
                        queue.Enqueue(next);
                }
            }
            return result;
        }

        /// <summary>
        /// Topological sort (algoritmo de Kahn).  Devuelve los nodos del grafo
        /// en orden topológico válido o null si el grafo contiene un ciclo.
        /// </summary>
        public List<TNode> TopologicalSortNodes()
        {
            var inDeg = new Dictionary<TNode, int>(ReferenceComparer);
            foreach (var n in Nodes) inDeg[n] = InDegree(n);

            var ready = new Queue<TNode>(inDeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var order = new List<TNode>(Nodes.Count);
            while (ready.Count > 0)
            {
                var n = ready.Dequeue();
                order.Add(n);
                foreach (var succ in Successors(n))
                {
                    if (!inDeg.ContainsKey(succ)) continue;
                    if (--inDeg[succ] == 0) ready.Enqueue(succ);
                }
            }
            return order.Count == Nodes.Count ? order : null;
        }

        /// <summary>
        /// Topological sort sobre aristas: orden válido para procesar tramos
        /// donde cada arista aparece después de las aristas que llegan a su nodo
        /// origen.  Devuelve null si hay ciclos.
        /// </summary>
        public List<TEdge> TopologicalSortEdges()
        {
            var nodeOrder = TopologicalSortNodes();
            if (nodeOrder == null) return null;

            var nodeIndex = new Dictionary<TNode, int>(ReferenceComparer);
            for (int i = 0; i < nodeOrder.Count; i++) nodeIndex[nodeOrder[i]] = i;

            return Edges
                .OrderBy(e =>
                {
                    var s = GetEdgeStart(e);
                    return s != null && nodeIndex.TryGetValue(s, out var idx) ? idx : int.MaxValue;
                })
                .ToList();
        }

        /// <summary>True si el grafo dirigido tiene al menos un ciclo.</summary>
        public bool HasCycle() => TopologicalSortNodes() == null;

        /// <summary>True si todos los nodos son alcanzables desde la fuente.</summary>
        public bool IsConnectedFromSource()
        {
            if (SourceNode == null) return Nodes.Count == 0;
            var reached = new HashSet<TNode>(ReferenceComparer);
            foreach (var n in Bfs(SourceNode)) reached.Add(n);
            return reached.Count == Nodes.Count;
        }

        // --------------------------------------------------------------------
        //  Helpers internos
        // --------------------------------------------------------------------

        private void EnsureAdapters()
        {
            if (GetEdgeStart == null || GetEdgeEnd == null)
                throw new InvalidOperationException(
                    "NetworkGraph requiere GetEdgeStart y GetEdgeEnd antes de operar con aristas.");
        }

        private static readonly IEqualityComparer<TNode> ReferenceComparer = ReferenceComparerImpl.Instance;

        private sealed class ReferenceComparerImpl : IEqualityComparer<TNode>
        {
            public static readonly ReferenceComparerImpl Instance = new();
            public bool Equals(TNode x, TNode y) => ReferenceEquals(x, y);
            public int GetHashCode(TNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
