using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Lee y recrea la definición (categorías + árbol de reglas) de un
/// <see cref="ParameterFilterElement"/> en formato portable entre proyectos:
/// categorías por BuiltInCategory, parámetros por BuiltInParameter / GUID /
/// nombre y valores ElementId por clase + nombre.
/// </summary>
internal static class FilterDefinitionSerializer
{
    private static readonly JsonSerializerOptions CompareOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ------------------------------------------------------------------ //
    //  Revit -> DTO
    // ------------------------------------------------------------------ //

    /// <summary><c>null</c> si no es un filtro de reglas o contiene reglas no soportadas.</summary>
    public static FilterDefinitionDto? Read(Document doc, FilterElement filter)
    {
        if (filter is not ParameterFilterElement pfe) return null;
        try
        {
            var dto = new FilterDefinitionDto
            {
                Categories = pfe.GetCategories()
                    .Select(id => EnumName(typeof(BuiltInCategory), id.Value))
                    .Where(n => n is not null)
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList(),
            };

            var ef = pfe.GetElementFilter();
            dto.Filter = ef is null ? null : ReadNode(doc, ef);
            return dto;
        }
        catch (Exception)
        {
            // Reglas no soportadas o API inesperada: el filtro se exporta solo con grafismo.
            return null;
        }
    }

    private static FilterNodeDto ReadNode(Document doc, ElementFilter f)
    {
        switch (f)
        {
            case LogicalAndFilter and:
                return new FilterNodeDto { Kind = "And", Children = and.GetFilters().Select(c => ReadNode(doc, c)).ToList() };
            case LogicalOrFilter or:
                return new FilterNodeDto { Kind = "Or", Children = or.GetFilters().Select(c => ReadNode(doc, c)).ToList() };
            case ElementParameterFilter epf:
                return new FilterNodeDto
                {
                    Kind     = "Param",
                    Inverted = epf.Inverted,
                    Rules    = epf.GetRules().Select(r => ReadRule(doc, r)).ToList(),
                };
            default:
                throw new NotSupportedException(f.GetType().Name);
        }
    }

    private static FilterRuleDto ReadRule(Document doc, FilterRule rule)
    {
        bool inverse = false;
        while (rule is FilterInverseRule inv)
        {
            inverse = !inverse;
            rule = inv.GetInnerRule();
        }

        var dto = new FilterRuleDto { Inverse = inverse, Parameter = ReadParam(doc, rule.GetRuleParameter()) };

        switch (rule)
        {
            case FilterStringRule s:
                dto.Type = "String";
                dto.Evaluator = EvaluatorName(s.GetEvaluator());
                dto.String = s.RuleString;
                break;
            case FilterDoubleRule d:
                dto.Type = "Double";
                dto.Evaluator = EvaluatorName(d.GetEvaluator());
                dto.Double = d.RuleValue;
                dto.Epsilon = d.Epsilon;
                break;
            case FilterIntegerRule i:
                dto.Type = "Integer";
                dto.Evaluator = EvaluatorName(i.GetEvaluator());
                dto.Integer = i.RuleValue;
                break;
            case FilterElementIdRule e:
                dto.Type = "ElementId";
                dto.Evaluator = EvaluatorName(e.GetEvaluator());
                dto.Element = ReadElementValue(doc, e.RuleValue);
                break;
            case HasValueFilterRule:
                dto.Type = "HasValue";
                break;
            case HasNoValueFilterRule:
                dto.Type = "HasNoValue";
                break;
            default:
                throw new NotSupportedException(rule.GetType().Name);
        }
        return dto;
    }

    /// <summary>
    /// Nombre del valor de un enum de Revit a partir de un ElementId negativo.
    /// BuiltInCategory/BuiltInParameter son Int32 en la API 2025 e Int64 en la
    /// 2026, y este ensamblado (compilado contra 2025) corre en ambas: nada de
    /// casts <c>(int)</c> ni Enum.IsDefined con un int; Enum.ToObject se adapta
    /// al tipo subyacente real en tiempo de ejecución.
    /// </summary>
    private static string? EnumName(Type enumType, long value)
    {
        try { return Enum.GetName(enumType, Enum.ToObject(enumType, value)); }
        catch { return null; }
    }

    /// <summary>Inverso de <see cref="EnumName"/>: nombre del enum → ElementId (vía long).</summary>
    private static ElementId? EnumId(Type enumType, string name)
    {
        try { return new ElementId(Convert.ToInt64(Enum.Parse(enumType, name))); }
        catch { return null; }
    }

    private static string EvaluatorName(object evaluator)
    {
        var n = evaluator.GetType().Name;           // FilterStringEquals, FilterNumericGreater…
        if (n.StartsWith("FilterString", StringComparison.Ordinal))  return n.Substring("FilterString".Length);
        if (n.StartsWith("FilterNumeric", StringComparison.Ordinal)) return n.Substring("FilterNumeric".Length);
        return n;
    }

    private static ParamRefDto ReadParam(Document doc, ElementId id)
    {
        if (id.Value < 0)
        {
            var bipName = EnumName(typeof(BuiltInParameter), id.Value)
                ?? throw new NotSupportedException($"BuiltInParameter {id.Value}");
            string? name = null;
            try { name = LabelUtils.GetLabelFor(Enum.Parse<BuiltInParameter>(bipName)); } catch { }
            return new ParamRefDto { BuiltIn = bipName, Name = name };
        }

        return doc.GetElement(id) switch
        {
            SharedParameterElement sp => new ParamRefDto { Guid = sp.GuidValue, Name = sp.Name },
            ParameterElement pe       => new ParamRefDto { Name = pe.Name },
            _                         => throw new NotSupportedException($"Parámetro {id.Value}"),
        };
    }

    private static ElementRefDto ReadElementValue(Document doc, ElementId id)
    {
        if (id.Value < 0 || doc.GetElement(id) is not Element e)
            return new ElementRefDto { RawId = id.Value };
        return new ElementRefDto { Class = e.GetType().FullName, Name = e.Name };
    }

    // ------------------------------------------------------------------ //
    //  DTO -> Revit
    // ------------------------------------------------------------------ //

    /// <summary>Crea el filtro. Devuelve <c>null</c> y el motivo si no se puede.</summary>
    public static ParameterFilterElement? Create(Document doc, string name, FilterDefinitionDto def, out string? error)
    {
        error = null;
        if (!TryBuild(doc, def, out var cats, out var filter, out error)) return null;
        try
        {
            return filter is null
                ? ParameterFilterElement.Create(doc, name, cats)
                : ParameterFilterElement.Create(doc, name, cats, filter);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Sustituye categorías y reglas de un filtro existente.</summary>
    public static bool Update(Document doc, ParameterFilterElement pfe, FilterDefinitionDto def, out string? error)
    {
        if (!TryBuild(doc, def, out var cats, out var filter, out error)) return false;
        try
        {
            pfe.SetCategories(cats);
            if (filter is null) pfe.ClearRules();
            else pfe.SetElementFilter(filter);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Compara dos definiciones (mismas categorías y mismas reglas).</summary>
    public static bool AreEquivalent(FilterDefinitionDto? a, FilterDefinitionDto? b)
        => a is not null && b is not null &&
           JsonSerializer.Serialize(a, CompareOpts) == JsonSerializer.Serialize(b, CompareOpts);

    private static bool TryBuild(Document doc, FilterDefinitionDto def,
        out ICollection<ElementId> cats, out ElementFilter? filter, out string? error)
    {
        cats = new List<ElementId>();
        filter = null;
        error = null;

        foreach (var c in def.Categories)
        {
            if (EnumId(typeof(BuiltInCategory), c) is { } bic) cats.Add(bic);
            else { error = $"Categoría desconocida: {c}"; return false; }
        }
        if (cats.Count == 0) { error = "El filtro no tiene categorías."; return false; }

        try
        {
            filter = def.Filter is null ? null : BuildNode(doc, def.Filter);
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }

        if (filter is not null &&
            !ParameterFilterElement.ElementFilterIsAcceptableForParameterFilterElement(doc, new HashSet<ElementId>(cats), filter))
        {
            error = "Las reglas no son válidas para esas categorías en este proyecto.";
            return false;
        }
        return true;
    }

    private static ElementFilter BuildNode(Document doc, FilterNodeDto n)
    {
        switch (n.Kind)
        {
            case "And":
                return new LogicalAndFilter((n.Children ?? new()).Select(c => BuildNode(doc, c)).ToList());
            case "Or":
                return new LogicalOrFilter((n.Children ?? new()).Select(c => BuildNode(doc, c)).ToList());
            default:
                var rules = (n.Rules ?? new()).Select(r => BuildRule(doc, r)).ToList();
                return new ElementParameterFilter(rules, n.Inverted);
        }
    }

    private static FilterRule BuildRule(Document doc, FilterRuleDto r)
    {
        var paramId = ResolveParam(doc, r.Parameter)
            ?? throw new InvalidOperationException($"Parámetro no encontrado en el proyecto: {r.Parameter}");
        var provider = new ParameterValueProvider(paramId);

        FilterRule rule = r.Type switch
        {
            "String"     => new FilterStringRule(provider, StringEvaluator(r.Evaluator), r.String ?? string.Empty),
            "Double"     => new FilterDoubleRule(provider, NumericEvaluator(r.Evaluator), r.Double ?? 0, r.Epsilon ?? 1e-6),
            "Integer"    => new FilterIntegerRule(provider, NumericEvaluator(r.Evaluator), r.Integer ?? 0),
            "ElementId"  => new FilterElementIdRule(provider, NumericEvaluator(r.Evaluator), ResolveElementValue(doc, r.Element)),
            "HasValue"   => ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId),
            "HasNoValue" => ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId),
            _            => throw new InvalidOperationException($"Tipo de regla no soportado: {r.Type}"),
        };

        return r.Inverse ? new FilterInverseRule(rule) : rule;
    }

    private static FilterStringRuleEvaluator StringEvaluator(string? name) => name switch
    {
        "Equals"         => new FilterStringEquals(),
        "BeginsWith"     => new FilterStringBeginsWith(),
        "EndsWith"       => new FilterStringEndsWith(),
        "Contains"       => new FilterStringContains(),
        "Greater"        => new FilterStringGreater(),
        "GreaterOrEqual" => new FilterStringGreaterOrEqual(),
        "Less"           => new FilterStringLess(),
        "LessOrEqual"    => new FilterStringLessOrEqual(),
        _ => throw new InvalidOperationException($"Evaluador de texto no soportado: {name}"),
    };

    private static FilterNumericRuleEvaluator NumericEvaluator(string? name) => name switch
    {
        "Equals"         => new FilterNumericEquals(),
        "Greater"        => new FilterNumericGreater(),
        "GreaterOrEqual" => new FilterNumericGreaterOrEqual(),
        "Less"           => new FilterNumericLess(),
        "LessOrEqual"    => new FilterNumericLessOrEqual(),
        _ => throw new InvalidOperationException($"Evaluador numérico no soportado: {name}"),
    };

    private static ElementId? ResolveParam(Document doc, ParamRefDto p)
    {
        if (p.BuiltIn is not null)
            return EnumId(typeof(BuiltInParameter), p.BuiltIn);

        if (p.Guid is Guid g && SharedParameterElement.Lookup(doc, g) is { } sp)
            return sp.Id;

        if (p.Name is null) return null;
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterElement))
            .FirstOrDefault(e => string.Equals(e.Name, p.Name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static ElementId ResolveElementValue(Document doc, ElementRefDto? v)
    {
        if (v is null) return ElementId.InvalidElementId;
        if (v.RawId is long raw) return new ElementId(raw);

        var type = v.Class is null ? null : typeof(Element).Assembly.GetType(v.Class);
        if (type is null || !typeof(Element).IsAssignableFrom(type))
            throw new InvalidOperationException($"Tipo de valor no soportado: {v.Class}");

        Element? match;
        try
        {
            match = new FilteredElementCollector(doc).OfClass(type)
                .FirstOrDefault(e => string.Equals(e.Name, v.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Clases que OfClass no admite: búsqueda lineal.
            match = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .UnionWith(new FilteredElementCollector(doc).WhereElementIsElementType())
                .FirstOrDefault(e => type.IsInstanceOfType(e) &&
                                     string.Equals(e.Name, v.Name, StringComparison.OrdinalIgnoreCase));
        }
        return match?.Id
            ?? throw new InvalidOperationException($"Valor no encontrado en el proyecto: {v.Name} ({type?.Name ?? v.Class})");
    }
}
