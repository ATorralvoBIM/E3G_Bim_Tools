using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.OneParameter;

namespace E3G.BimTools.DiTools.Core.Services.OneParameter;

/// <summary>
/// Implementacion de <see cref="IOneParameterService"/> para Sprint 10.
///
/// GetParameterNames: muestrea hasta 8 elementos por categoria de modelo para
/// recopilar nombres de parametros; es rapido incluso en modelos grandes.
///
/// Search: itera todos los elementos (o los de las categorias del filtro) y
/// retiene los que tienen el parametro.  Para modelos con &gt;50.000 elementos
/// el escaneo puede tardar 1-2 segundos; se acepta ese coste porque la operacion
/// es puntual.
///
/// ApplyEdits / FindReplace: todo en una unica transaccion via ITransactionService
/// para que un solo Ctrl-Z deshaga el cambio completo.
/// </summary>
public sealed class OneParameterService : IOneParameterService
{
    private readonly ITransactionService _tx;
    private readonly ILogger _logger;

    public OneParameterService(ITransactionService tx, ILoggerFactory loggerFactory)
    {
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
        _logger = loggerFactory?.CreateLogger<OneParameterService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // ====================================================================
    // GetParameterNames
    // ====================================================================

    public IReadOnlyList<string> GetParameterNames(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        const int SamplePerCategory = 8;

        foreach (Category cat in doc.Settings.Categories)
        {
            if (cat.CategoryType != CategoryType.Model) continue;
            try
            {
                int count = 0;
                var col = new FilteredElementCollector(doc)
                    .OfCategoryId(cat.Id)
                    .WhereElementIsNotElementType();

                foreach (var el in col)
                {
                    if (count++ >= SamplePerCategory) break;
                    foreach (Parameter p in el.Parameters)
                    {
                        var name = p.Definition?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name!);
                    }
                }
            }
            catch { /* categoria sin colector valido; ignorar */ }
        }

        return names.ToList();
    }

    // ====================================================================
    // Search
    // ====================================================================

    public OneParameterSearchResult Search(Document doc, OneParameterSearchRequest request)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var result = new OneParameterSearchResult();
        if (string.IsNullOrWhiteSpace(request.ParameterName)) return result;

        var categoriesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<Element> elements = CollectElements(doc, request);

        foreach (var el in elements)
        {
            result.ElementsScanned++;
            var p = el.LookupParameter(request.ParameterName);
            if (p is null) continue;

            var row = BuildRow(doc, el, p, request.ParameterName);
            result.Rows.Add(row);

            if (el.Category?.Name is { } catName)
                categoriesSeen.Add(catName);
        }

        result.CategoriesFound = categoriesSeen.Count;
        _logger.Information(
            "OneParameter Search '{0}': {1}", (object)request.ParameterName, (object)result.ToSummary());
        return result;
    }

    private static IEnumerable<Element> CollectElements(
        Document doc, OneParameterSearchRequest request)
    {
        // 1. Obtener el conjunto base segun el scope.
        IEnumerable<Element> baseElements = request.Scope switch
        {
            OneParameterScope.CurrentView      => CollectFromView(doc, request),
            OneParameterScope.CurrentSelection => CollectFromSelection(doc, request),
            _                                  => CollectFromModel(doc, request),
        };

        // 2. Aplicar filtro de categorias si procede.
        var hasFilter = request.CategoryFilter is { Count: > 0 };
        if (!hasFilter) return baseElements;

        var allowed = new HashSet<BuiltInCategory>(request.CategoryFilter!);
        return baseElements.Where(el =>
        {
            if (el.Category is null) return false;
            try { return allowed.Contains((BuiltInCategory)el.Category.Id.Value); }
            catch { return false; }
        });
    }

    /// <summary>Todos los elementos del modelo (scope AllModel).</summary>
    private static IEnumerable<Element> CollectFromModel(
        Document doc, OneParameterSearchRequest request)
    {
        var hasFilter = request.CategoryFilter is { Count: > 0 };

        // Sin filtro de categoria: recoger por tipo (instancias y/o tipos).
        if (!hasFilter)
        {
            if (!request.IncludeTypeElements)
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();

            var instances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();
            var types = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToElements()
                .Where(e => e.Category != null);
            return instances.Concat(types);
        }

        // Con filtro de categorias: multi-OR sobre BuiltInCategory.
        var filters = new List<ElementFilter>();
        foreach (var bic in request.CategoryFilter!)
        {
            try { filters.Add(new ElementCategoryFilter(bic)); }
            catch { /* BIC no valida; saltar */ }
        }
        if (filters.Count == 0) return Enumerable.Empty<Element>();

        ElementFilter combined = filters.Count == 1
            ? filters[0]
            : new LogicalOrFilter(filters);

        var collector = new FilteredElementCollector(doc).WherePasses(combined);
        if (!request.IncludeTypeElements)
            collector = collector.WhereElementIsNotElementType();

        return collector.ToElements();
    }

    /// <summary>Solo los elementos visibles en la vista activa (scope CurrentView).</summary>
    private static IEnumerable<Element> CollectFromView(
        Document doc, OneParameterSearchRequest request)
    {
        var viewId = request.ActiveViewId;
        if (viewId is null || viewId == ElementId.InvalidElementId)
            return Enumerable.Empty<Element>(); // sin vista, sin resultados

        var collector = new FilteredElementCollector(doc, viewId)
            .WhereElementIsNotElementType();

        return collector.ToElements();
    }

    /// <summary>Solo los elementos actualmente seleccionados (scope CurrentSelection).</summary>
    private static IEnumerable<Element> CollectFromSelection(
        Document doc, OneParameterSearchRequest request)
    {
        if (request.SelectedElementIds is not { Count: > 0 })
            return Enumerable.Empty<Element>();

        var result = new List<Element>(request.SelectedElementIds.Count);
        foreach (var id in request.SelectedElementIds)
        {
            try
            {
                var el = doc.GetElement(id);
                if (el is not null) result.Add(el);
            }
            catch { /* id invalido */ }
        }
        return result;
    }

    private static OneParameterRow BuildRow(
        Document doc, Element el, Parameter p, string paramName)
    {
        Element? typeEl = null;
        try
        {
            var tid = el.GetTypeId();
            if (tid != ElementId.InvalidElementId)
                typeEl = doc.GetElement(tid);
        }
        catch { }

        string level = string.Empty;
        try
        {
            if (el.LevelId != ElementId.InvalidElementId)
                level = doc.GetElement(el.LevelId)?.Name ?? string.Empty;
        }
        catch { }

        var currentVal = GetStringValue(p);
        return new OneParameterRow
        {
            ElementId   = el.Id.Value,
            Category    = el.Category?.Name ?? string.Empty,
            Family      = (typeEl as FamilySymbol)?.FamilyName ?? string.Empty,
            TypeName    = typeEl?.Name ?? string.Empty,
            ElementName = el.Name,
            Level       = level,
            CurrentValue = currentVal,
            EditedValue  = currentVal,
            IsReadOnly   = p.IsReadOnly,
            ParameterName = paramName,
        };
    }

    private static string GetStringValue(Parameter p)
    {
        try
        {
            return p.StorageType switch
            {
                StorageType.String    => p.AsString() ?? string.Empty,
                StorageType.Integer   => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                StorageType.Double    => p.AsDouble().ToString(CultureInfo.InvariantCulture),
                StorageType.ElementId => p.AsElementId()?.Value.ToString() ?? string.Empty,
                _                     => p.AsValueString() ?? string.Empty,
            };
        }
        catch { return string.Empty; }
    }

    // ====================================================================
    // ApplyEdits
    // ====================================================================

    public OneParameterEditResult ApplyEdits(Document doc, IReadOnlyList<OneParameterRow> rows)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (rows is null) throw new ArgumentNullException(nameof(rows));

        var result = new OneParameterEditResult();
        var pending = rows.Where(r => r.IsModified && !r.IsReadOnly).ToList();
        if (pending.Count == 0) return result;

        _tx.Run(doc, "OneParameter: Aplicar ediciones", d =>
        {
            foreach (var row in pending)
            {
                var el = d.GetElement(new ElementId(row.ElementId));
                if (el is null) { result.Skipped++; continue; }

                var p = el.LookupParameter(row.ParameterName);
                if (p is null || p.IsReadOnly) { result.Skipped++; continue; }

                if (TrySetValue(p, row.EditedValue, result))
                {
                    row.CurrentValue = row.EditedValue;
                    row.IsModified   = false;
                    result.Modified++;
                }
                else
                {
                    result.Skipped++;
                }
            }
        });

        _logger.Information("OneParameter ApplyEdits: {0}", (object)result.ToSummary());
        return result;
    }

    // ====================================================================
    // FindReplace
    // ====================================================================

    public OneParameterEditResult FindReplace(
        Document                        doc,
        string                          parameterName,
        string                          findText,
        string                          replaceText,
        bool                            matchCase,
        IReadOnlyList<BuiltInCategory>? categoryFilter)
    {
        if (doc is null)           throw new ArgumentNullException(nameof(doc));
        if (parameterName is null) throw new ArgumentNullException(nameof(parameterName));
        if (findText is null)      throw new ArgumentNullException(nameof(findText));

        var result = new OneParameterEditResult();
        if (string.IsNullOrEmpty(findText)) return result;

        var comparison = matchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        // Recopilar candidatos: solo instancias con parametro de tipo String.
        var request = new OneParameterSearchRequest
        {
            ParameterName  = parameterName,
            CategoryFilter = categoryFilter,
        };
        IEnumerable<Element> elements = CollectElements(doc, request);

        var toEdit = new List<(ElementId id, string newVal)>();

        foreach (var el in elements)
        {
            var p = el.LookupParameter(parameterName);
            if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) continue;

            var current = p.AsString() ?? string.Empty;
            if (!current.Contains(findText, comparison)) continue;

            var newVal = matchCase
                ? current.Replace(findText, replaceText ?? string.Empty,
                      StringComparison.Ordinal)
                : ReplaceInsensitive(current, findText, replaceText ?? string.Empty);

            toEdit.Add((el.Id, newVal));
        }

        if (toEdit.Count == 0) return result;

        _tx.Run(doc, $"OneParameter: Reemplazar '{findText}' → '{replaceText}'", d =>
        {
            foreach (var (id, newVal) in toEdit)
            {
                var el = d.GetElement(id);
                if (el is null) { result.Skipped++; continue; }
                var p = el.LookupParameter(parameterName);
                if (p is null || p.IsReadOnly) { result.Skipped++; continue; }
                try
                {
                    if (p.Set(newVal)) result.Modified++;
                    else result.Skipped++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Error en {id.Value}: {ex.Message}");
                    result.Skipped++;
                }
            }
        });

        _logger.Information(
            "OneParameter FindReplace '{0}'->'{1}': {2}",
            (object)findText, (object)(replaceText ?? string.Empty), (object)result.ToSummary());
        return result;
    }

    // ====================================================================
    // PreviewTransform
    // ====================================================================

    public TransformPreviewResult PreviewTransform(
        IReadOnlyList<OneParameterRow> rows,
        TransformRequest               request)
    {
        if (rows is null)    throw new ArgumentNullException(nameof(rows));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var result = new TransformPreviewResult();

        foreach (var row in rows)
        {
            if (row.IsReadOnly) continue;

            var preview = new TransformPreviewRow
            {
                ElementId   = row.ElementId,
                Category    = row.Category,
                ElementName = row.ElementName,
                Level       = row.Level,
                Before      = row.CurrentValue,
            };

            try
            {
                preview.After = ComputeTransform(row.CurrentValue, request);
            }
            catch (Exception ex)
            {
                preview.After  = row.CurrentValue; // sin cambio si hay error
                preview.Error  = ex.Message;
                result.Warnings.Add($"Elemento {row.ElementId}: {ex.Message}");
            }

            result.Rows.Add(preview);
        }

        return result;
    }

    // ====================================================================
    // ApplyTransform
    // ====================================================================

    public OneParameterEditResult ApplyTransform(
        Document                       doc,
        IReadOnlyList<OneParameterRow> rows,
        TransformRequest               request)
    {
        if (doc is null)     throw new ArgumentNullException(nameof(doc));
        if (rows is null)    throw new ArgumentNullException(nameof(rows));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var result  = new OneParameterEditResult();
        var toWrite = new List<(OneParameterRow row, string newVal)>();

        // Calcular nuevos valores (sin transaccion).
        foreach (var row in rows)
        {
            if (row.IsReadOnly) continue;
            try
            {
                var newVal = ComputeTransform(row.CurrentValue, request);
                if (!string.Equals(newVal, row.CurrentValue, StringComparison.Ordinal))
                    toWrite.Add((row, newVal));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Elemento {row.ElementId}: {ex.Message}");
                result.Skipped++;
            }
        }

        if (toWrite.Count == 0) return result;

        var txName = request.Operation switch
        {
            TransformOperation.Prefix       => $"OneParameter: Prefix '{request.Prefix}'",
            TransformOperation.Suffix       => $"OneParameter: Suffix '{request.Suffix}'",
            TransformOperation.Concat       => $"OneParameter: Concat '{request.Prefix}…{request.Suffix}'",
            TransformOperation.RegexReplace => $"OneParameter: Regex '{request.Pattern}' → '{request.Replacement}'",
            _                               => $"OneParameter: Transformar '{request.ParameterName}'",
        };

        _tx.Run(doc, txName, d =>
        {
            foreach (var (row, newVal) in toWrite)
            {
                var el = d.GetElement(new ElementId(row.ElementId));
                if (el is null) { result.Skipped++; continue; }
                var p = el.LookupParameter(request.ParameterName);
                if (p is null || p.IsReadOnly) { result.Skipped++; continue; }

                try
                {
                    if (p.StorageType == StorageType.String && p.Set(newVal))
                    {
                        row.CurrentValue = newVal;
                        row.EditedValue  = newVal;
                        row.IsModified   = false;
                        result.Modified++;
                    }
                    else result.Skipped++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Error en {row.ElementId}: {ex.Message}");
                    result.Skipped++;
                }
            }
        });

        _logger.Information("OneParameter ApplyTransform ({0}): {1}",
            (object)request.Operation, (object)result.ToSummary());
        return result;
    }

    /// <summary>Calcula el nuevo valor de texto segun la operacion de transformacion.</summary>
    private static string ComputeTransform(string current, TransformRequest req)
    {
        return req.Operation switch
        {
            TransformOperation.SetValue =>
                req.Prefix,                           // Prefix actua como el valor nuevo

            TransformOperation.Prefix =>
                req.Prefix + current,

            TransformOperation.Suffix =>
                current + req.Suffix,

            TransformOperation.Concat =>
                req.Prefix + current + req.Suffix,

            TransformOperation.RegexReplace =>
                ApplyRegex(current, req),

            _ => current,
        };
    }

    private static string ApplyRegex(string input, TransformRequest req)
    {
        if (string.IsNullOrEmpty(req.Pattern)) return input;
        var opts = req.MatchCase
            ? RegexOptions.None
            : RegexOptions.IgnoreCase;
        // Tiempo maximo de 1 s para evitar ReDos.
        var rx = new Regex(req.Pattern, opts, TimeSpan.FromSeconds(1));
        return rx.Replace(input, req.Replacement ?? string.Empty);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static string ReplaceInsensitive(string input, string find, string replace)
    {
        var sb = new StringBuilder(input.Length);
        int start = 0;
        while (true)
        {
            int idx = input.IndexOf(find, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                sb.Append(input, start, input.Length - start);
                break;
            }
            sb.Append(input, start, idx - start);
            sb.Append(replace);
            start = idx + find.Length;
        }
        return sb.ToString();
    }

    private static bool TrySetValue(Parameter p, string val, OneParameterEditResult result)
    {
        try
        {
            return p.StorageType switch
            {
                StorageType.String    => p.Set(val ?? string.Empty),
                StorageType.Integer   => TrySetInt(p, val, result),
                StorageType.Double    => TrySetDouble(p, val, result),
                StorageType.ElementId => TrySetElementId(p, val, result),
                _                     => p.Set(val ?? string.Empty),
            };
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error: {ex.Message}");
            return false;
        }
    }

    private static bool TrySetInt(Parameter p, string raw, OneParameterEditResult r)
    {
        if (!int.TryParse((raw ?? string.Empty).Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            if (string.Equals(raw, "true",  StringComparison.OrdinalIgnoreCase)) v = 1;
            else if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) v = 0;
            else { r.Warnings.Add($"Entero no valido: '{raw}'."); return false; }
        }
        return p.Set(v);
    }

    private static bool TrySetDouble(Parameter p, string raw, OneParameterEditResult r)
    {
        var clean = (raw ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            r.Warnings.Add($"Decimal no valido: '{raw}'.");
            return false;
        }
        return p.Set(v);
    }

    private static bool TrySetElementId(Parameter p, string raw, OneParameterEditResult r)
    {
        if (!long.TryParse((raw ?? string.Empty).Trim(), out var v))
        {
            r.Warnings.Add($"ElementId no valido: '{raw}'.");
            return false;
        }
        return p.Set(new ElementId(v));
    }
}
