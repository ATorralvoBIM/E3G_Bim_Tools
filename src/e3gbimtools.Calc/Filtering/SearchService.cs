// ============================================================================
//  SearchService.cs
//  Parser de queries libres a FilterCriteria.  Sintaxis soportada (todos
//  los tokens son opcionales y se combinan con AND):
//
//      bfs:N         → BfsIndex exacto
//      bfs:N-M       → rango (ambos extremos inclusive)
//      id:12345678   → RevitId exacto
//      dn:50         → DN exacto (admite "DN50", "DN 50", "50")
//      level:P2      → nivel cuyo nombre contenga "P2" (case-insensitive)
//      status:error  → severidad (ok / warning / error / aviso)
//      crit          → solo camino crítico
//      nofit         → ocultar fittings
//      issues        → solo Warning/Error
//
//  Cualquier token sin prefijo conocido se acumula como texto libre y se
//  busca en NodePath / Status / Size.
//
//  Ejemplo:
//      "bfs:3-8 dn:40 level:P2 issues"
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace e3gbimtools.Calc.Filtering
{
    public static class SearchService
    {
        public static FilterCriteria Parse(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return FilterCriteria.Empty;

            string freeText = null;
            int bfsFrom = -1, bfsTo = -1;
            long elementId = -1;
            int dnExact = 0;
            var levels = new List<string>();
            var severities = new List<string>();
            bool onlyCrit = false, hideFit = false, onlyIssues = false;
            var freeSb = new StringBuilder();

            // Split por espacios respetando comillas simples.
            foreach (var tok in Tokenize(query))
            {
                if (string.IsNullOrWhiteSpace(tok)) continue;
                int colon = tok.IndexOf(':');
                if (colon < 0)
                {
                    // Token suelto: puede ser flag corto o texto libre.
                    var key = tok.Trim().ToLowerInvariant();
                    switch (key)
                    {
                        case "crit":
                        case "critical":
                        case "critico":
                            onlyCrit = true; break;
                        case "nofit":
                        case "nofittings":
                            hideFit = true; break;
                        case "issues":
                        case "problemas":
                            onlyIssues = true; break;
                        default:
                            AppendFree(freeSb, tok);
                            break;
                    }
                    continue;
                }

                var head = tok.Substring(0, colon).Trim().ToLowerInvariant();
                var val  = tok.Substring(colon + 1).Trim();
                if (val.Length == 0) continue;

                switch (head)
                {
                    case "bfs":
                    case "n":
                        ParseRange(val, out bfsFrom, out bfsTo);
                        break;
                    case "id":
                    case "elementid":
                    case "revitid":
                        if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                            elementId = id;
                        break;
                    case "dn":
                    case "size":
                        dnExact = ParseDn(val);
                        break;
                    case "level":
                    case "nivel":
                        levels.Add(val);
                        break;
                    case "status":
                    case "severity":
                    case "estado":
                        severities.Add(NormalizeSeverity(val));
                        break;
                    default:
                        // Prefijo desconocido → tratar como texto libre
                        AppendFree(freeSb, tok);
                        break;
                }
            }

            freeText = freeSb.Length > 0 ? freeSb.ToString().Trim() : null;
            return new FilterCriteria(
                freeText: freeText,
                bfsFrom: bfsFrom, bfsTo: bfsTo,
                elementId: elementId,
                dnExact: dnExact,
                levels: levels.Count > 0 ? levels.AsReadOnly() : null,
                severities: severities.Count > 0 ? severities.AsReadOnly() : null,
                onlyCriticalPath: onlyCrit,
                hideFittings: hideFit,
                onlyIssues: onlyIssues);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static void ParseRange(string val, out int from, out int to)
        {
            from = to = -1;
            int dash = val.IndexOf('-');
            if (dash > 0)
            {
                if (int.TryParse(val.Substring(0, dash), out var a)) from = a;
                if (int.TryParse(val.Substring(dash + 1), out var b)) to   = b;
                if (from > to && to >= 0) (from, to) = (to, from);
            }
            else if (int.TryParse(val, out var n))
            {
                from = to = n;
            }
        }

        /// <summary>"50", "DN50", "DN 50" → 50.</summary>
        private static int ParseDn(string val)
        {
            var sb = new StringBuilder();
            foreach (var c in val) if (char.IsDigit(c)) sb.Append(c);
            return int.TryParse(sb.ToString(), out var n) ? n : 0;
        }

        private static string NormalizeSeverity(string val)
        {
            switch (val.Trim().ToLowerInvariant())
            {
                case "ok":      return "OK";
                case "warn":
                case "warning":
                case "aviso":   return "Warning";
                case "err":
                case "error":
                case "fail":    return "Error";
                default:        return val;
            }
        }

        private static void AppendFree(StringBuilder sb, string token)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(token);
        }

        /// <summary>Tokeniza respetando comillas simples y dobles.</summary>
        private static IEnumerable<string> Tokenize(string s)
        {
            var sb = new StringBuilder();
            char? quote = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (quote.HasValue)
                {
                    if (c == quote.Value) quote = null;
                    else sb.Append(c);
                }
                else if (c == '"' || c == '\'')
                {
                    quote = c;
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }
    }
}
