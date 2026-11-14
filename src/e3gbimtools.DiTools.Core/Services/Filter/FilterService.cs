using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Filter;

namespace E3G.BimTools.DiTools.Core.Services.Filter;

/// <summary>
/// Implementacion del motor OneFilter.
///
/// Decisiones de diseño:
///  - Las categorias se leen via <c>Document.Settings.Categories</c> filtrando
///    las que no son visibles en el documento (no aportan valor al filtrar).
///  - Para listar parametros se toma un elemento muestra de cada categoria y
///    se enumeran sus parametros de instancia + del tipo asociado.
///  - La evaluacion AND/OR es recursiva: <see cref="EvaluateGroup"/> aplica la
///    logica del grupo y delega cada condicion a <see cref="EvaluateCondition"/>.
///  - Los valores numericos del usuario aceptan tanto coma como punto decimal
///    (locale es-ES); se convierten a invariant antes de comparar.
/// </summary>
public sealed class FilterService : IFilterService
{
    private readonly ILogger _logger;

    public FilterService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<FilterService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // =====================================================================
    // Categorias
    // =====================================================================

    public IReadOnlyList<CategoryDescriptor> GetCategories(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new List<CategoryDescriptor>();
        foreach (Category cat in doc.Settings.Categories)
        {
            if (cat is null) continue;
            if (string.IsNullOrWhiteSpace(cat.Name)) continue;

            // Solo categorias modelables (con BuiltInCategory util).
            BuiltInCategory bic;
            try { bic = (BuiltInCategory)cat.Id.Value; }
            catch { continue; }
            if (bic == BuiltInCategory.INVALID) continue;

            result.Add(new CategoryDescriptor
            {
                Name = cat.Name,
                BuiltInCategory = bic,
                CategoryId = cat.Id.Value,
            });
        }

        return result
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // =====================================================================
    // Parametros disponibles
    // =====================================================================

    public IReadOnlyList<ParameterDescriptor> GetParametersForCategories(
        Document doc,
        IReadOnlyList<BuiltInCategory> categories)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (categories is null || categories.Count == 0)
            return Array.Empty<ParameterDescriptor>();

        var instanceParams = new Dictionary<string, ParameterDescriptor>(StringComparer.OrdinalIgnoreCase);
        var typeParams = new Dictionary<string, ParameterDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var bic in categories)
        {
            try
            {
                // Tomamos hasta 5 elementos por categoria como muestra — suficiente para
                // capturar la mayoria de parametros sin recorrer todo el modelo.
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .Take(5)
                    .ToList();

                foreach (var el in elements)
                {
                    CollectParameters(el, isType: false, instanceParams);

                    var typeEl = doc.GetElement(el.GetTypeId());
                    if (typeEl is not null)
                        CollectParameters(typeEl, isType: true, typeParams);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("OneFilter: error leyendo params de {0}: {1}", bic, ex.Message);
            }
        }

        var all = instanceParams.Values
            .Concat(typeParams.Values)
            .OrderBy(p => p.IsTypeParameter)             // primero instancia, luego tipo
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return all;
    }

    private static void CollectParameters(Element el, bool isType, Dictionary<string, ParameterDescriptor> dict)
    {
        foreach (Parameter p in el.Parameters)
        {
            if (p is null) continue;
            var def = p.Definition;
            if (def is null) continue;
            var name = def.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var key = (isType ? "T::" : "I::") + name;
            if (dict.ContainsKey(key)) continue;

            dict[key] = new ParameterDescriptor
            {
                Name = name,
                DisplayName = isType ? $"(Tipo) {name}" : name,
                IsTypeParameter = isType,
                StorageTypeName = p.StorageType.ToString(),
            };
        }
    }

    // =====================================================================
    // Evaluacion del filtro
    // =====================================================================

    public FilterResult Evaluate(Document doc, FilterDefinition definition)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (definition is null) throw new ArgumentNullException(nameof(definition));

        var result = new FilterResult();
        var categories = definition.GetCategories().ToList();
        if (categories.Count == 0)
        {
            result.Warnings.Add("No has seleccionado ninguna categoria.");
            return result;
        }

        result.CategoriesEvaluated = categories.Count;

        foreach (var bic in categories)
        {
            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType();

                foreach (var el in collector)
                {
                    result.ElementsEvaluated++;
                    if (EvaluateGroup(el, definition.Root, result))
                        result.MatchedIds.Add(el.Id);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Error al recorrer la categoria {bic}: {ex.Message}");
            }
        }

        _logger.Information("OneFilter Evaluate: {0}", result.ToSummary());
        return result;
    }

    private bool EvaluateGroup(Element el, FilterGroup group, FilterResult result)
    {
        if (group is null || group.IsEmpty) return true;

        if (group.Logic == FilterLogic.And)
        {
            foreach (var c in group.Conditions)
                if (!EvaluateCondition(el, c, result)) return false;
            foreach (var sg in group.SubGroups)
                if (!EvaluateGroup(el, sg, result)) return false;
            return true;
        }
        else // OR
        {
            foreach (var c in group.Conditions)
                if (EvaluateCondition(el, c, result)) return true;
            foreach (var sg in group.SubGroups)
                if (EvaluateGroup(el, sg, result)) return true;
            return false;
        }
    }

    private bool EvaluateCondition(Element el, FilterCondition c, FilterResult result)
    {
        if (c is null || !c.IsValid()) return true;  // condicion vacia = no excluye

        Parameter? p = null;
        try
        {
            if (c.UseTypeParameter)
            {
                var typeEl = el.Document.GetElement(el.GetTypeId());
                p = typeEl?.LookupParameter(c.ParameterName);
            }
            else
            {
                // Primero busca en la instancia; si no, prueba el tipo.
                p = el.LookupParameter(c.ParameterName);
                if (p is null)
                {
                    var typeEl = el.Document.GetElement(el.GetTypeId());
                    p = typeEl?.LookupParameter(c.ParameterName);
                }
            }
        }
        catch { /* ignoramos: tratamos como parametro inexistente */ }

        // Si el parametro no existe, la condicion solo se cumple con IsEmpty.
        if (p is null)
            return c.Operator == FilterOperator.IsEmpty;

        // Empty-checks atajan antes de leer valor.
        var paramText = ReadAsText(p);
        if (c.Operator == FilterOperator.IsEmpty) return string.IsNullOrEmpty(paramText);
        if (c.Operator == FilterOperator.IsNotEmpty) return !string.IsNullOrEmpty(paramText);

        return p.StorageType switch
        {
            StorageType.Double  => EvalDouble(p, c, result),
            StorageType.Integer => EvalInteger(p, c, result),
            _                   => EvalText(paramText, c.Value, c.Operator),
        };
    }

    // ---------- Comparadores tipados ---------------------------------------

    private static bool EvalText(string actual, string expected, FilterOperator op)
    {
        actual ??= string.Empty;
        expected ??= string.Empty;
        var sc = StringComparison.CurrentCultureIgnoreCase;

        return op switch
        {
            FilterOperator.Equal       => actual.Equals(expected, sc),
            FilterOperator.NotEqual    => !actual.Equals(expected, sc),
            FilterOperator.Contains    => actual.IndexOf(expected, sc) >= 0,
            FilterOperator.NotContains => actual.IndexOf(expected, sc) < 0,
            FilterOperator.StartsWith  => actual.StartsWith(expected, sc),
            FilterOperator.EndsWith    => actual.EndsWith(expected, sc),
            // Ordenacion lexicografica para Greater/Less sobre texto — util en codigos tipo "A1" < "A2".
            FilterOperator.Greater         => string.Compare(actual, expected, sc) > 0,
            FilterOperator.GreaterOrEqual  => string.Compare(actual, expected, sc) >= 0,
            FilterOperator.Less            => string.Compare(actual, expected, sc) < 0,
            FilterOperator.LessOrEqual     => string.Compare(actual, expected, sc) <= 0,
            _ => false,
        };
    }

    private static bool EvalDouble(Parameter p, FilterCondition c, FilterResult result)
    {
        if (!TryParseNumber(c.Value, out double expected))
        {
            result.Warnings.Add($"Valor numerico no valido para '{c.ParameterName}': '{c.Value}'.");
            return false;
        }
        var actual = p.AsDouble();
        return CompareNumeric(actual, expected, c.Operator);
    }

    private static bool EvalInteger(Parameter p, FilterCondition c, FilterResult result)
    {
        if (!TryParseNumber(c.Value, out double expected))
        {
            result.Warnings.Add($"Valor numerico no valido para '{c.ParameterName}': '{c.Value}'.");
            return false;
        }
        double actual = p.AsInteger();
        return CompareNumeric(actual, expected, c.Operator);
    }

    private static bool CompareNumeric(double a, double b, FilterOperator op)
    {
        const double eps = 1e-9;
        return op switch
        {
            FilterOperator.Equal           => Math.Abs(a - b) < eps,
            FilterOperator.NotEqual        => Math.Abs(a - b) >= eps,
            FilterOperator.Greater         => a > b + eps,
            FilterOperator.GreaterOrEqual  => a >= b - eps,
            FilterOperator.Less            => a < b - eps,
            FilterOperator.LessOrEqual     => a <= b + eps,
            // Contains/StartsWith/EndsWith sobre numero — comparamos via representacion textual.
            FilterOperator.Contains        => a.ToString("R", CultureInfo.InvariantCulture).Contains(b.ToString("R", CultureInfo.InvariantCulture)),
            FilterOperator.NotContains     => !a.ToString("R", CultureInfo.InvariantCulture).Contains(b.ToString("R", CultureInfo.InvariantCulture)),
            _ => false,
        };
    }

    private static bool TryParseNumber(string raw, out double value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = 0; return false; }
        var normalized = raw.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string ReadAsText(Parameter p)
    {
        try
        {
            // AsValueString respeta la unidad/visualizacion del proyecto (m, mm, etc.).
            // Para texto/integer/elementId puede devolver null; caemos al AsString/AsInteger.
            var asVal = p.AsValueString();
            if (!string.IsNullOrEmpty(asVal)) return asVal;
        }
        catch { /* sigue */ }

        return p.StorageType switch
        {
            StorageType.String   => p.AsString() ?? string.Empty,
            StorageType.Integer  => p.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.Double   => p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
            StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
            _                    => string.Empty,
        };
    }

    // =====================================================================
    // Filas de resultado
    // =====================================================================

    public IReadOnlyList<FilterRow> BuildRows(Document doc, IReadOnlyList<ElementId> ids)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (ids is null) return Array.Empty<FilterRow>();

        var rows = new List<FilterRow>(ids.Count);
        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            if (el is null) continue;

            string family = string.Empty;
            string type = string.Empty;
            try
            {
                var typeEl = doc.GetElement(el.GetTypeId());
                if (typeEl is ElementType et)
                {
                    family = et.FamilyName ?? string.Empty;
                    type = et.Name ?? string.Empty;
                }
                else if (typeEl is not null)
                {
                    type = typeEl.Name ?? string.Empty;
                }
            }
            catch { /* ignora */ }

            rows.Add(new FilterRow
            {
                ElementId = el.Id.Value,
                Category = el.Category?.Name ?? string.Empty,
                Family = family,
                Type = type,
                Name = el.Name ?? string.Empty,
                LevelName = ResolveLevelName(doc, el),
            });
        }
        return rows;
    }

    private static string ResolveLevelName(Document doc, Element el)
    {
        try
        {
            var lvlIdParam = el.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                ?? el.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)
                ?? el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (lvlIdParam is not null && lvlIdParam.StorageType == StorageType.ElementId)
            {
                var lvlId = lvlIdParam.AsElementId();
                if (lvlId is not null && lvlId != ElementId.InvalidElementId)
                {
                    var lvl = doc.GetElement(lvlId) as Level;
                    if (lvl is not null) return lvl.Name;
                }
            }
        }
        catch { /* fallback */ }
        return string.Empty;
    }
}
