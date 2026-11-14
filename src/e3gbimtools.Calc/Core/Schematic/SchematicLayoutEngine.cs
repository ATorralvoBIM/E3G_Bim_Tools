// ============================================================================
//  SchematicLayoutEngine.cs
//  Construye un SchematicLayout a partir de la lista de ResultRow ya ordenada
//  en DFS preorder branch-first y del mapa columnOf.  Replica la lógica de
//  BuildSchematic (CalcWindow) pero en coordenadas reales en mm — para que
//  el resultado se pueda renderizar tanto en WPF como en Revit con paridad.
//
//  Coordenadas:
//    - Origen (0,0) arriba a la izquierda.
//    - X crece a la derecha (hacia ramales).
//    - Y crece HACIA ABAJO (estilo dibujo arquitectónico: source arriba).
//    - El renderer Revit invertirá Y al convertir a feet porque Y en
//      ViewDrafting crece hacia arriba.
// ============================================================================
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ResultRow = e3gbimtools.Calc.Core.Common.ResultRow;

namespace e3gbimtools.Calc.Core.Schematic
{
    public sealed class SchematicLayoutEngine
    {
        public SchematicLayout Build(
            IList<ResultRow> rows,
            IReadOnlyDictionary<ResultRow, int> columnOf,
            SchematicParameters parameters,
            ISet<ResultRow> criticalPathRows = null,
            SchematicLegend legend = null)
        {
            var p = parameters ?? new SchematicParameters();
            var crit = criticalPathRows ?? new HashSet<ResultRow>();

            var nodes = new List<SchematicNode>();
            var conns = new List<SchematicConnection>();
            var juncs = new List<SchematicJunction>();

            // Mapa nodo→posición para conectar pipes con sus padres (igual que
            // BuildSchematic).  Almacena la última X/Y registrada del nodo.
            var nodePos = new Dictionary<string, (double X, double Y, int Col)>();

            // Mapa nodo→fitting que termina ahí (TEE/CROSS) → usado para
            // poner el glifo en el punto exacto de bifurcación.
            var fittingByNode = new Dictionary<string, ResultRow>();
            foreach (var r in rows)
            {
                if (r == null || !r.IsFitting) continue;
                string at = GetIncomingNode(r.NodePath);
                if (!string.IsNullOrEmpty(at)) fittingByNode[at] = r;
            }

            double maxX = 0;
            var sourcePos = new SchematicPoint(p.MmTrunkX, p.MmTopMargin);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;

                int col = (columnOf != null && columnOf.TryGetValue(row, out int c)) ? c : 0;
                double x = p.MmTrunkX + col * p.MmPerColumn;
                double y = p.MmTopMargin + (i + 1) * p.MmPerRow;
                if (x > maxX) maxX = x;

                string incoming = GetIncomingNode(row.NodePath);
                string outgoing = GetOutgoingNode(row.NodePath);

                double pX, pY;
                if (!string.IsNullOrEmpty(incoming) && nodePos.TryGetValue(incoming, out var pp))
                { pX = pp.X; pY = pp.Y; }
                else
                { pX = p.MmTrunkX; pY = p.MmTopMargin; }

                bool isCont = System.Math.Abs(pX - x) < 0.01;
                bool onCrit = crit.Contains(row);

                if (isCont)
                {
                    var conn = new SchematicConnection(
                        new SchematicPoint(x, pY),
                        new SchematicPoint(x, y),
                        SchematicConnectionKind.Continuation, row)
                    { OnCriticalPath = onCrit };
                    conns.Add(conn);
                }
                else
                {
                    var conn = new SchematicConnection(
                        new SchematicPoint(pX, y),
                        new SchematicPoint(x, y),
                        SchematicConnectionKind.Bifurcation, row)
                    { OnCriticalPath = onCrit };
                    conns.Add(conn);

                    ResultRow parentFit = null;
                    if (!string.IsNullOrEmpty(incoming))
                        fittingByNode.TryGetValue(incoming, out parentFit);
                    juncs.Add(new SchematicJunction(
                        new SchematicPoint(pX, y), parentFit) { OnCriticalPath = onCrit });
                }

                bool isTerminal = row.IsFitting && row.EndNodeKind == "TERMINAL";
                bool isBifurcation = row.IsFitting
                    && (row.EndNodeKind == "TEE" || row.EndNodeKind == "CROSS");
                string label = BuildLabel(row, p);

                nodes.Add(new SchematicNode(
                    row, new SchematicPoint(x, y), col,
                    row.IsFitting, isTerminal, isBifurcation, label)
                { OnCriticalPath = onCrit });

                if (!string.IsNullOrEmpty(outgoing))
                    nodePos[outgoing] = (x, y, col);
            }

            double widthMm = maxX + p.MmPerColumn + 80;     // margen derecha para texto
            double heightMm = p.MmTopMargin + (rows.Count + 1) * p.MmPerRow + 40;

            return new SchematicLayout(nodes, conns, juncs, sourcePos,
                legend, widthMm, heightMm, p);
        }

        private static string BuildLabel(ResultRow row, SchematicParameters p)
        {
            if (row.IsFitting)
            {
                if (!p.ShowFittingLabels) return "";
                return (row.EndNodeKind ?? "") + " " + (row.NodePath ?? "");
            }
            if (!p.ShowPipeLabels) return "";
            var sb = new StringBuilder();
            sb.Append('#').Append(row.BfsIndex);
            if (!string.IsNullOrEmpty(row.NodePath))
                sb.Append(" · ").Append(row.NodePath);
            if (!string.IsNullOrEmpty(row.Size))
                sb.Append(" · ").Append(row.Size);
            sb.Append(" · ").Append(row.FlowLps.ToString("F2",
                CultureInfo.InvariantCulture)).Append(" L/s");
            return sb.ToString();
        }

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
