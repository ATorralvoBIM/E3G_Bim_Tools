// ============================================================================
//  CriticalPathService.cs
//  Calcula el camino crítico — recorrido fuente → terminal con mayor ΔP
//  acumulado — sobre una lista de ResultRow ya calculada.
//
//  Complejidad temporal: O(N) donde N = número de pipes.
//  Complejidad espacial: O(N) para los diccionarios + O(D) para el path,
//                        D = profundidad del árbol.
//
//  Pre-condiciones:
//    - rows está en orden DFS preorder branch-first (lo que ya produce
//      ReorderDfsBranchFirst), por lo que cuando procesamos un row su
//      padre ya está en el diccionario cumulative.
//    - Cada ResultRow pipe lleva TotalLoss en Pa con las pérdidas del
//      tramo + las de su fitting downstream agregadas.
//
//  Robustez:
//    - Si rows está vacío, devuelve un Result vacío.
//    - Si la red tiene varios árboles desconectados (red mal montada), se
//      procesa cada árbol independientemente y se devuelve el peor de todos.
//    - Si TotalLoss es negativo (raro), se trata como 0 para no romper
//      el algoritmo.
// ============================================================================
using System.Collections.Generic;
using System.Linq;
using ResultRow = e3gbimtools.Calc.Core.Common.ResultRow;

namespace e3gbimtools.Calc.Core.Analytics
{
    public sealed class CriticalPathService
    {
        public CriticalPathResult Analyze(IList<ResultRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return new CriticalPathResult(
                    new List<ResultRow>(), null, 0.0,
                    new Dictionary<ResultRow, double>());

            // ── 1) indexar pipes por nodo de salida ────────────────────────
            var rowByOutgoing = new Dictionary<string, ResultRow>(rows.Count);
            var allPipes = new List<ResultRow>(rows.Count);
            foreach (var r in rows)
            {
                if (r == null || r.IsFitting) continue;
                allPipes.Add(r);
                string outgoing = GetOutgoingNode(r.NodePath);
                if (!string.IsNullOrEmpty(outgoing))
                    rowByOutgoing[outgoing] = r;
            }
            if (allPipes.Count == 0)
                return new CriticalPathResult(
                    new List<ResultRow>(), null, 0.0,
                    new Dictionary<ResultRow, double>());

            // ── 2) acumular ΔP en orden de la lista (ya es DFS preorder) ──
            //    El padre de cada pipe ya está en cumulative al procesarlo.
            var cumulative = new Dictionary<ResultRow, double>(allPipes.Count);
            var hasChildren = new HashSet<ResultRow>();
            foreach (var p in allPipes)
            {
                double baseDp = 0.0;
                string incoming = GetIncomingNode(p.NodePath);
                if (!string.IsNullOrEmpty(incoming)
                    && rowByOutgoing.TryGetValue(incoming, out var parent)
                    && cumulative.TryGetValue(parent, out var parentDp))
                {
                    baseDp = parentDp;
                    hasChildren.Add(parent);
                }
                double thisLoss = p.TotalLoss > 0 ? p.TotalLoss : 0.0;
                cumulative[p] = baseDp + thisLoss;
            }

            // ── 3) identificar terminales (pipes sin hijos) ────────────────
            var terminals = allPipes.Where(p => !hasChildren.Contains(p)).ToList();
            if (terminals.Count == 0)
                return new CriticalPathResult(
                    new List<ResultRow>(), null, 0.0, cumulative);

            // ── 4) argmax de los terminales por ΔP acumulado ──────────────
            ResultRow worst = terminals[0];
            double worstDp = cumulative[worst];
            for (int i = 1; i < terminals.Count; i++)
            {
                double dp = cumulative[terminals[i]];
                if (dp > worstDp) { worst = terminals[i]; worstDp = dp; }
            }

            // ── 5) trace back: del terminal al origen ─────────────────────
            var path = new List<ResultRow>();
            var visited = new HashSet<ResultRow>();
            var current = worst;
            while (current != null && visited.Add(current))
            {
                path.Add(current);
                string incoming = GetIncomingNode(current.NodePath);
                if (string.IsNullOrEmpty(incoming)) break;
                rowByOutgoing.TryGetValue(incoming, out current);
            }
            path.Reverse();

            return new CriticalPathResult(path, worst, worstDp, cumulative);
        }

        // Helpers idénticos a los de CalcWindow para no introducir
        // dependencias cruzadas (mantenemos esta capa libre de WPF).
        private static string GetIncomingNode(string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;
            if (nodePath.StartsWith("@")) return nodePath.Substring(1).Trim();
            int idx = nodePath.IndexOf('→');
            if (idx > 0) return nodePath.Substring(0, idx).Trim();
            return nodePath.Trim();
        }

        private static string GetOutgoingNode(string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;
            if (nodePath.StartsWith("@")) return nodePath.Substring(1).Trim();
            int idx = nodePath.IndexOf('→');
            if (idx > 0 && idx + 1 < nodePath.Length)
                return nodePath.Substring(idx + 1).Trim();
            return nodePath.Trim();
        }
    }
}
