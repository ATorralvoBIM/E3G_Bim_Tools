using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion de <see cref="IRevitParameterInspector"/>.
/// </summary>
public sealed class RevitParameterInspector : IRevitParameterInspector
{
    public CategoryParameters GetParameters(Document doc, BuiltInCategory category)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        var result = new CategoryParameters();

        if (category == BuiltInCategory.INVALID) return result;

        // Primer elemento de la categoria (sin tipos).
        var sample = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .FirstOrDefault();

        if (sample is null) return result;

        // Parametros de instancia.
        var instanceSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Parameter p in sample.Parameters)
        {
            if (p is null) continue;
            var name = p.Definition?.Name;
            if (string.IsNullOrEmpty(name)) continue;
            if (!instanceSeen.Add(name)) continue;
            result.Instance.Add(new ParameterInfo
            {
                Name = name,
                StorageType = p.StorageType,
                IsInstance = true,
            });
        }

        // Parametros de tipo (a traves del Symbol/Type).
        var typeId = sample.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeElement = doc.GetElement(typeId);
            if (typeElement is not null)
            {
                var typeSeen = new HashSet<string>(StringComparer.Ordinal);
                foreach (Parameter p in typeElement.Parameters)
                {
                    if (p is null) continue;
                    var name = p.Definition?.Name;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!typeSeen.Add(name)) continue;
                    result.Type.Add(new ParameterInfo
                    {
                        Name = name,
                        StorageType = p.StorageType,
                        IsInstance = false,
                    });
                }
            }
        }

        // Orden alfabetico estable.
        result.Instance.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        result.Type.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetParameterValues(Document doc, BuiltInCategory category, string parameterName)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (string.IsNullOrWhiteSpace(parameterName)) return Array.Empty<string>();
        if (category == BuiltInCategory.INVALID) return Array.Empty<string>();

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var collector = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType();

        foreach (Element e in collector)
        {
            var raw = ReadValue(e, parameterName);
            if (!string.IsNullOrEmpty(raw)) values.Add(raw);
        }

        return values
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ReadValue(Element element, string parameterName)
    {
        // Instancia.
        var p = element.LookupParameter(parameterName);
        var raw = ParamToString(p);
        if (!string.IsNullOrEmpty(raw)) return raw;

        // Tipo.
        var typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeEl = element.Document.GetElement(typeId);
            if (typeEl is not null)
            {
                var tp = typeEl.LookupParameter(parameterName);
                return ParamToString(tp);
            }
        }
        return string.Empty;
    }

    private static string ParamToString(Parameter? p)
    {
        if (p is null || !p.HasValue) return string.Empty;
        return p.StorageType switch
        {
            StorageType.String   => p.AsString() ?? string.Empty,
            StorageType.Integer  => p.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.Double   => p.AsValueString() ?? p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
            StorageType.ElementId => p.AsValueString() ?? (p.AsElementId()?.ToString() ?? string.Empty),
            _ => p.AsValueString() ?? string.Empty,
        };
    }
}
