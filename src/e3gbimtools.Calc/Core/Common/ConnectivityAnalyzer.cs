// ============================================================================
//  ConnectivityAnalyzer.cs
//  Core.Common — Localiza el PUNTO EXACTO de una desconexión en una red.
//
//  Una red de cálculo debe ser un árbol conectado desde la fuente.  Cuando hay
//  un hueco (un codo/te/unión sin conectar, un tubo borrado…), la red se parte
//  en componentes: el conectado a la fuente y uno o varios "huérfanos".
//
//  En vez de marcar TODO el componente huérfano (ruidoso), este analizador
//  identifica el TRAMO EXACTO del corte: por cada componente huérfano busca el
//  nudo geométricamente MÁS CERCANO al lado conectado a la fuente (ese es el
//  punto de rotura) y devuelve los tramos incidentes a ese nudo.  El resto del
//  componente se devuelve como "aguas abajo del corte" (consecuencia, no causa).
//
//  Genérico (sirve para tuberías y conductos) mediante delegados.
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;

namespace e3gbimtools.Calc.Core.Common
{
    /// <summary>Resultado del análisis de cortes de una red.</summary>
    public sealed class BreakAnalysis
    {
        /// <summary>Ids (RevitId) de los tramos EXACTOS donde está el corte.</summary>
        public HashSet<long> BreakSegmentIds { get; } = new HashSet<long>();

        /// <summary>Ids de los tramos huérfanos que cuelgan aguas abajo de un corte.</summary>
        public HashSet<long> DownstreamSegmentIds { get; } = new HashSet<long>();

        /// <summary>Distancia [m] del corte al lado conectado (diagnóstico; por componente, la menor).</summary>
        public double NearestGapM { get; set; }
    }

    /// <summary>
    /// Analizador de conectividad genérico.  Trabaja sobre cualquier par
    /// nodo/arista mediante delegados, de modo que tanto la red hidráulica como
    /// la aeráulica lo reutilizan sin duplicar la lógica.
    /// </summary>
    public static class ConnectivityAnalyzer
    {
        /// <summary>
        /// Detecta los tramos exactos donde la red se corta de la fuente.
        /// </summary>
        /// <param name="nodes">Todos los nudos de la red.</param>
        /// <param name="edges">Todos los tramos de la red.</param>
        /// <param name="reachable">Nudos alcanzables desde la fuente (BFS dirigido).</param>
        /// <param name="edgesOf">Tramos conectados a un nudo.</param>
        /// <param name="startOf">Nudo inicial de un tramo.</param>
        /// <param name="endOf">Nudo final de un tramo.</param>
        /// <param name="idOf">RevitId del tramo.</param>
        /// <param name="x">Coordenada X del nudo [m].</param>
        /// <param name="y">Coordenada Y del nudo [m].</param>
        /// <param name="z">Coordenada Z del nudo [m].</param>
        public static BreakAnalysis FindBreaks<TNode, TEdge>(
            IEnumerable<TNode> nodes,
            IEnumerable<TEdge> edges,
            HashSet<TNode> reachable,
            Func<TNode, IEnumerable<TEdge>> edgesOf,
            Func<TEdge, TNode> startOf,
            Func<TEdge, TNode> endOf,
            Func<TEdge, long> idOf,
            Func<TNode, double> x,
            Func<TNode, double> y,
            Func<TNode, double> z)
            where TNode : class
        {
            var result = new BreakAnalysis { NearestGapM = double.PositiveInfinity };
            if (nodes == null || edges == null || reachable == null) return result;

            // Nudos huérfanos (no alcanzables) y conectados (referencia geométrica).
            var orphanNodes    = new List<TNode>();
            var reachableNodes = new List<TNode>();
            foreach (var n in nodes)
            {
                if (n == null) continue;
                if (reachable.Contains(n)) reachableNodes.Add(n);
                else orphanNodes.Add(n);
            }
            if (orphanNodes.Count == 0 || reachableNodes.Count == 0) return result;

            // Componentes conexas entre los nudos huérfanos (BFS no dirigido).
            var comp = new Dictionary<TNode, int>(ReferenceComparer<TNode>.Instance);
            int nComp = 0;
            foreach (var seed in orphanNodes)
            {
                if (comp.ContainsKey(seed)) continue;
                int id = nComp++;
                comp[seed] = id;
                var stack = new Stack<TNode>();
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    var u = stack.Pop();
                    foreach (var e in edgesOf(u))
                    {
                        var s = startOf(e);
                        var v = ReferenceEquals(s, u) ? endOf(e) : s;
                        if (v == null || reachable.Contains(v) || comp.ContainsKey(v)) continue;
                        comp[v] = id;
                        stack.Push(v);
                    }
                }
            }

            // Nudo de entrada por componente = el huérfano más cercano al lado
            // conectado a la fuente (= el punto de rotura).
            var entryNode = new TNode[nComp];
            var entryDist = new double[nComp];
            for (int i = 0; i < nComp; i++) entryDist[i] = double.PositiveInfinity;

            foreach (var kv in comp)
            {
                TNode on = kv.Key;
                int id   = kv.Value;
                double best = double.PositiveInfinity;
                double ox = x(on), oy = y(on), oz = z(on);
                foreach (var rn in reachableNodes)
                {
                    double dx = ox - x(rn), dy = oy - y(rn), dz = oz - z(rn);
                    double d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 < best) best = d2;
                }
                if (best < entryDist[id]) { entryDist[id] = best; entryNode[id] = on; }
            }

            var entrySet = new HashSet<TNode>(ReferenceComparer<TNode>.Instance);
            for (int i = 0; i < nComp; i++)
            {
                if (entryNode[i] == null) continue;
                entrySet.Add(entryNode[i]);
                double gap = Math.Sqrt(entryDist[i]);
                if (gap < result.NearestGapM) result.NearestGapM = gap;
            }

            // Clasificar cada tramo huérfano: incidente a un nudo de entrada =
            // tramo EXACTO del corte; el resto = aguas abajo.
            foreach (var e in edges)
            {
                var s = startOf(e);
                var t = endOf(e);
                bool orphan = (s != null && !reachable.Contains(s))
                           || (t != null && !reachable.Contains(t));
                if (!orphan) continue;

                long id = idOf(e);
                if ((s != null && entrySet.Contains(s)) || (t != null && entrySet.Contains(t)))
                    result.BreakSegmentIds.Add(id);
                else
                    result.DownstreamSegmentIds.Add(id);
            }

            if (double.IsPositiveInfinity(result.NearestGapM)) result.NearestGapM = 0.0;
            return result;
        }

        // Comparador por referencia (los nudos no sobreescriben Equals).
        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
