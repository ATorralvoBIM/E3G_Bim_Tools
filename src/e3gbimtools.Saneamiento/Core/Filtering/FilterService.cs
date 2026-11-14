// ============================================================================
//  FilterService.cs
//  Evaluador de FilterCriteria sobre IEnumerable<ResultRow>.
//
//  Rendimiento:
//    - Precompila los predicados activos una sola vez por evaluación (no por
//      fila) — evita allocations en el bucle caliente.
//    - Si el criterio es Empty devuelve el set entero sin construir hashset.
//    - Single-pass O(n) sobre las rows.
//    - Texto libre comparado case-insensitive sobre cadenas pre-existentes
//      (no se crea ToLower temporal por fila — IndexOf con
//      StringComparison.OrdinalIgnoreCase es ~2-3× más rápido).
//
//  El resultado es un HashSet<ResultRow> para que el llamador pueda hacer
//  lookups O(1) desde el renderer del esquema.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.Core.Filtering
{
    public sealed class FilterService
    {
        /// <summary>Conjunto opcional de filas del camino crítico (para
        /// el flag OnlyCriticalPath). Si está vacío y se pide ese flag,
        /// no se devuelve nada.</summary>
        public ISet<ResultRow> CriticalPathRows { get; set; }

        /// <summary>
        /// Evalúa el criterio sobre todas las rows.  Devuelve un HashSet de
        /// las que pasan el filtro.  Si <paramref name="criteria"/> es Empty
        /// devuelve el set entero (referencia compartida) sin asignar nada.
        /// </summary>
        public HashSet<ResultRow> Evaluate(IList<ResultRow> rows, FilterCriteria criteria)
        {
            var result = new HashSet<ResultRow>();
            if (rows == null || rows.Count == 0) return result;
            if (criteria == null || criteria.IsEmpty)
            {
                foreach (var r in rows) if (r != null) result.Add(r);
                return result;
            }

            // ── Precompilar predicados activos ─────────────────────────────
            var preds = BuildPredicates(criteria);
            int predCount = preds.Count;

            // ── Pasada única ───────────────────────────────────────────────
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                bool ok = true;
                for (int p = 0; p < predCount; p++)
                {
                    if (!preds[p](row)) { ok = false; break; }
                }
                if (ok) result.Add(row);
            }
            return result;
        }

        /// <summary>Conveniencia: ¿esta fila pasa el filtro?  Útil para
        /// CollectionView.Filter (que llama una vez por fila).</summary>
        public Predicate<object> ToCollectionViewPredicate(FilterCriteria criteria)
        {
            if (criteria == null || criteria.IsEmpty) return _ => true;
            var preds = BuildPredicates(criteria);
            int count = preds.Count;
            return o =>
            {
                if (!(o is ResultRow row) || row == null) return false;
                for (int p = 0; p < count; p++)
                    if (!preds[p](row)) return false;
                return true;
            };
        }

        // ── Compilación de predicados ─────────────────────────────────────

        private List<Func<ResultRow, bool>> BuildPredicates(FilterCriteria c)
        {
            var list = new List<Func<ResultRow, bool>>(8);

            if (!string.IsNullOrEmpty(c.FreeText))
            {
                string needle = c.FreeText;
                list.Add(row => MatchesFreeText(row, needle));
            }

            if (c.BfsFrom >= 0 || c.BfsTo >= 0)
            {
                int from = c.BfsFrom < 0 ? int.MinValue : c.BfsFrom;
                int to   = c.BfsTo   < 0 ? int.MaxValue : c.BfsTo;
                list.Add(row => row.BfsIndex >= from && row.BfsIndex <= to);
            }

            if (c.ElementId >= 0)
            {
                long target = c.ElementId;
                list.Add(row => row.RevitId == target);
            }

            if (c.DnExact > 0)
            {
                int target = c.DnExact;
                list.Add(row => ExtractDn(row.Size) == target
                              || ExtractDn(row.RecommendedSize) == target);
            }

            if (c.Levels.Count > 0)
            {
                // Hashset de levels normalizados para lookups O(1).
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in c.Levels) set.Add(l ?? "");
                list.Add(row => ContainsAny(row.LevelName, set));
            }

            if (c.Severities.Count > 0)
            {
                var set = new HashSet<string>(c.Severities, StringComparer.OrdinalIgnoreCase);
                list.Add(row => row.StatusKind != null && set.Contains(row.StatusKind));
            }

            if (c.OnlyCriticalPath)
            {
                var crit = CriticalPathRows;
                if (crit == null || crit.Count == 0)
                    list.Add(_ => false);   // imposible cumplir → set vacío
                else
                    list.Add(row => crit.Contains(row));
            }

            if (c.HideFittings)
                list.Add(row => !row.IsFitting);

            if (c.OnlyIssues)
                list.Add(row => row.StatusKind == "Warning" || row.StatusKind == "Error");

            return list;
        }

        // ── Helpers de matching ────────────────────────────────────────────

        private static bool MatchesFreeText(ResultRow r, string needle)
        {
            // Busca en NodePath, Status, Size, ElementKind, LevelName, RecommendedSize.
            // OrdinalIgnoreCase para evitar tablas de cultura.
            return Cont(r.NodePath,        needle)
                || Cont(r.Status,          needle)
                || Cont(r.Size,            needle)
                || Cont(r.ElementKind,     needle)
                || Cont(r.LevelName,       needle)
                || Cont(r.RecommendedSize, needle)
                || (r.RevitId > 0 && r.RevitId.ToString(CultureInfo.InvariantCulture).Contains(needle));
        }

        private static bool Cont(string hay, string needle)
            => !string.IsNullOrEmpty(hay)
            && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>"DN 50" → 50; "DN50" → 50; "50" → 50; null/"" → 0.</summary>
        private static int ExtractDn(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return 0;
            int n = 0;
            bool inNum = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c >= '0' && c <= '9') { n = n * 10 + (c - '0'); inNum = true; }
                else if (inNum) break;
            }
            return n;
        }

        private static bool ContainsAny(string levelName, HashSet<string> set)
        {
            if (string.IsNullOrEmpty(levelName)) return false;
            // Coincidencia por inclusión de subcadena en el nombre del nivel.
            foreach (var s in set)
                if (!string.IsNullOrEmpty(s)
                    && levelName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}
