using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using E3G.BimTools.RevitBC3.Mapping;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion de <see cref="IMappingRuleEngine"/>.
///
/// Para cada filtro de la regla:
/// 1. Busca el parametro por nombre en el elemento (instancia).
/// 2. Si no esta, lo busca en el tipo del elemento.
/// 3. Aplica el operador con el valor configurado.
/// Si todos los filtros pasan -> la regla matchea.
/// </summary>
public sealed class MappingRuleEngine : IMappingRuleEngine
{
    public string GenerateDiagnostic(Document doc, MappingRule rule, int sampleCount = 5)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (rule is null) throw new ArgumentNullException(nameof(rule));

        var sb = new StringBuilder();
        sb.AppendLine("=== Diagnostico de regla ===");
        sb.AppendLine($"Nombre:    {(string.IsNullOrEmpty(rule.Name) ? "(sin nombre)" : rule.Name)}");
        sb.AppendLine($"Capitulo:  {rule.ChapterDisplay}");
        sb.AppendLine($"Categoria: {rule.CategoryDisplayName}  (BuiltInCategory={rule.Category})");
        sb.AppendLine($"Partida:   {rule.BC3Code}");
        sb.AppendLine($"Activa:    {(rule.Enabled ? "SI" : "NO ← desactiva, no entrara en el calculo")}");
        sb.AppendLine();

        if (rule.Filters.Count == 0)
        {
            sb.AppendLine("Filtros:   (ninguno — la regla matchea TODOS los elementos de la categoria)");
        }
        else
        {
            sb.AppendLine("Filtros:");
            foreach (var f in rule.Filters)
                sb.AppendLine($"   - {f}");
        }
        sb.AppendLine();

        if (rule.Category == BuiltInCategory.INVALID)
        {
            sb.AppendLine("ERROR: la regla no tiene una categoria valida.");
            return sb.ToString();
        }

        var collector = new FilteredElementCollector(doc)
            .OfCategory(rule.Category)
            .WhereElementIsNotElementType();

        int total = 0;
        int matches = 0;
        var nonMatchSamples = new System.Collections.Generic.List<string>();

        foreach (Element e in collector)
        {
            total++;
            if (Evaluate(e, rule))
            {
                matches++;
            }
            else if (nonMatchSamples.Count < sampleCount)
            {
                var sample = new StringBuilder();
                sample.Append("   Element ").Append(e.Id.Value)
                      .Append(" (tipo: ").Append(GetTypeNameSafe(e)).AppendLine(")");
                foreach (var filter in rule.Filters)
                {
                    var actual = ReadParameterRaw(e, filter.ParameterName);
                    var ok = CheckFilter(actual, filter);
                    sample.Append("     - ").Append(filter.ParameterName)
                          .Append(": leido=\"").Append(actual).Append('"')
                          .Append("  vs  filtro=\"").Append(filter.Value).Append('"')
                          .Append("  → ").AppendLine(ok ? "OK" : "NO");
                }
                nonMatchSamples.Add(sample.ToString());
            }
        }

        sb.AppendLine("RESUMEN:");
        sb.AppendLine($"   Elementos de la categoria: {total}");
        sb.AppendLine($"   Coinciden con la regla:    {matches}");
        sb.AppendLine($"   Sin coincidir:             {total - matches}");
        sb.AppendLine();

        if (nonMatchSamples.Count > 0)
        {
            sb.AppendLine($"EJEMPLOS DE ELEMENTOS QUE NO COINCIDEN (primeros {nonMatchSamples.Count}):");
            foreach (var s in nonMatchSamples) sb.AppendLine(s);
        }

        if (total > 0 && matches == 0 && rule.Filters.Count > 0)
        {
            sb.AppendLine("PISTA: ninguno coincide.  Compara los valores 'leido' arriba con tu");
            sb.AppendLine("       'filtro' — probablemente lo que escribiste no coincide con");
            sb.AppendLine("       lo que el modelo realmente tiene.  Edita la regla y pica el");
            sb.AppendLine("       valor del dropdown (no a mano).");
        }

        return sb.ToString();
    }

    private static string GetTypeNameSafe(Element e)
    {
        try
        {
            var typeId = e.GetTypeId();
            if (typeId == ElementId.InvalidElementId) return "(sin tipo)";
            var typeEl = e.Document.GetElement(typeId);
            return typeEl?.Name ?? "(sin nombre)";
        }
        catch { return "(error)"; }
    }

    public IReadOnlyList<ElementId> MatchElements(Document doc, MappingRule rule)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (rule is null) throw new ArgumentNullException(nameof(rule));

        var result = new List<ElementId>();
        if (rule.Category == BuiltInCategory.INVALID) return result;

        // Para evaluar usamos Evaluate, que ya respeta el flag Enabled.  Si la
        // regla esta desactivada igualmente devolvemos lista vacia — el usuario
        // puede activarla y reintentar.
        var collector = new FilteredElementCollector(doc)
            .OfCategory(rule.Category)
            .WhereElementIsNotElementType();

        foreach (Element e in collector)
        {
            if (Evaluate(e, rule)) result.Add(e.Id);
        }
        return result;
    }

    public bool Evaluate(Element element, MappingRule rule)
    {
        if (element is null || rule is null) return false;
        if (!rule.Enabled) return false;

        // 1. Categoria.
        var cat = element.Category;
        if (cat is null) return false;
        if (cat.BuiltInCategory != rule.Category) return false;

        // 2. Sin filtros -> matchea todos los elementos de la categoria.
        if (rule.Filters is null || rule.Filters.Count == 0) return true;

        // 3. Filtros (AND).
        foreach (var filter in rule.Filters)
        {
            var raw = ReadParameterRaw(element, filter.ParameterName);
            if (!CheckFilter(raw, filter)) return false;
        }

        return true;
    }

    /// <summary>
    /// Lee el parametro por nombre, primero en la instancia y si no esta
    /// (o esta vacio), en el tipo del elemento.  Esto MUST match exactamente
    /// la logica de RevitParameterInspector.ReadValue para que el sugeridor
    /// y el comparador devuelvan SIEMPRE el mismo string sobre el mismo
    /// elemento — si no, las reglas no matchearian nunca.
    /// </summary>
    private static string ReadParameterRaw(Element element, string parameterName)
    {
        if (string.IsNullOrEmpty(parameterName)) return string.Empty;

        // 1) Instancia: si tiene valor no vacio, lo devolvemos.
        var p = element.LookupParameter(parameterName);
        var raw = ParamToString(p);
        if (!string.IsNullOrEmpty(raw)) return raw;

        // 2) Tipo: fallback si la instancia no aporta nada.
        var typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeElement = element.Document.GetElement(typeId);
            if (typeElement is not null)
            {
                var tp = typeElement.LookupParameter(parameterName);
                return ParamToString(tp);
            }
        }

        return string.Empty;
    }

    private static string ParamToString(Parameter? p)
    {
        if (p is null || !p.HasValue) return string.Empty;

        // IMPORTANTE: la conversion a string debe coincidir con la que usa
        // RevitParameterInspector.GetParameterValues — si no, el sugeridor
        // ofreceria valores legibles ("MC_E3G_PEX") y el motor compararia
        // contra IDs internos ("1234567"), por lo que NUNCA matchearia.
        var raw = p.StorageType switch
        {
            StorageType.String   => p.AsString() ?? string.Empty,
            StorageType.Integer  => p.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.Double   => p.AsValueString() ?? p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
            StorageType.ElementId => p.AsValueString() ?? (p.AsElementId()?.ToString() ?? string.Empty),
            _ => p.AsValueString() ?? string.Empty,
        };

        // Trim defensivo: algunos parametros traen espacios al inicio/final
        // que romperian la comparacion exacta con lo que el usuario eligio
        // del dropdown (que tambien se trimea).
        return raw?.Trim() ?? string.Empty;
    }

    private static bool CheckFilter(string raw, MappingFilter filter)
    {
        var target = filter.Value ?? string.Empty;

        switch (filter.Operator)
        {
            case MappingOperator.Equals:
                // Comparacion EXACTA del string completo (case-insensitive).
                // No hacemos fallback numerico aqui: si el usuario quiere
                // comparar numericamente "125" vs "125 mm" debe usar un
                // operador numerico explicito (GreaterOrEqual, LessOrEqual,
                // etc.).  Sin esta regla estricta, valores como "10_20_Pluvials"
                // y "10_10_Fecals" se evaluan ambos como 10 -> matcheo falso.
                return string.Equals(raw.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase);

            case MappingOperator.NotEquals:
                return !string.Equals(raw.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase);

            case MappingOperator.Contains:
                return raw.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;

            case MappingOperator.StartsWith:
                return raw.StartsWith(target, StringComparison.OrdinalIgnoreCase);

            case MappingOperator.EndsWith:
                return raw.EndsWith(target, StringComparison.OrdinalIgnoreCase);

            case MappingOperator.Regex:
                try { return Regex.IsMatch(raw, target, RegexOptions.IgnoreCase); }
                catch { return false; }

            case MappingOperator.GreaterThan:
            case MappingOperator.GreaterOrEqual:
            case MappingOperator.LessThan:
            case MappingOperator.LessOrEqual:
                if (!TryDouble(raw, out var r) || !TryDouble(target, out var t)) return false;
                return filter.Operator switch
                {
                    MappingOperator.GreaterThan    => r > t,
                    MappingOperator.GreaterOrEqual => r >= t,
                    MappingOperator.LessThan       => r < t,
                    MappingOperator.LessOrEqual    => r <= t,
                    _ => false,
                };

            default:
                return false;
        }
    }

    /// <summary>
    /// Devuelve true si ambos lados, una vez stripeados de sufijos de unidad
    /// comunes (mm, cm, m, m2, m3, in, ft, kg, etc.), se pueden parsear como
    /// doubles y son iguales con una tolerancia pequena (1e-6 relativa).
    /// </summary>
    private static bool NumericEquals(string a, string b)
    {
        if (!TryDouble(a, out var x)) return false;
        if (!TryDouble(b, out var y)) return false;
        // Comparacion tolerante: epsilon absoluto pequeno + relativo 1e-6.
        var diff = Math.Abs(x - y);
        if (diff <= 1e-9) return true;
        var scale = Math.Max(Math.Abs(x), Math.Abs(y));
        return diff <= scale * 1e-6;
    }

    private static bool TryDouble(string s, out double v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Quita sufijos de unidad comunes (case-insensitive) y caracteres no numericos
        // al final.  Asi "125 mm", "125mm", "125,0 mm", "125 mm²" -> "125".
        var clean = s.Trim().Replace(',', '.');

        // Recorta sufijo: deja solo prefijo numerico (signos, digitos, punto, exponente).
        int i = 0;
        if (i < clean.Length && (clean[i] == '+' || clean[i] == '-')) i++;
        bool seenDigit = false;
        bool seenDot = false;
        while (i < clean.Length)
        {
            var c = clean[i];
            if (c >= '0' && c <= '9') { seenDigit = true; i++; continue; }
            if (c == '.' && !seenDot) { seenDot = true; i++; continue; }
            // 'e'/'E' iniciaria un exponente — soportar.
            if ((c == 'e' || c == 'E') && seenDigit)
            {
                i++;
                if (i < clean.Length && (clean[i] == '+' || clean[i] == '-')) i++;
                while (i < clean.Length && clean[i] >= '0' && clean[i] <= '9') i++;
                break;
            }
            break;
        }
        if (!seenDigit) return false;

        // Validacion estricta del sufijo: lo que sigue al prefijo numerico solo
        // puede ser espacios o sufijo de unidad reconocible (letras tipo "mm",
        // "kg", simbolos "²"/"³"/"°"/"/"  para m², m³, °C, kg/m...).  Si aparece
        // un simbolo como "_", "-" en posicion no-signo, "@", etc., entonces la
        // cadena NO es un numero (es texto que casualmente empieza con cifras,
        // tipo "10_20_Pluvials").  En ese caso NumericEquals devolveria false
        // positivo y romperia la comparacion exacta del filtro Equals.
        for (int j = i; j < clean.Length; j++)
        {
            var c = clean[j];
            if (char.IsWhiteSpace(c)) continue;
            if (char.IsLetter(c)) continue;
            if (c == '²' || c == '³' || c == '°' || c == '/') continue;
            return false;
        }

        var numeric = clean.Substring(0, i);
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
