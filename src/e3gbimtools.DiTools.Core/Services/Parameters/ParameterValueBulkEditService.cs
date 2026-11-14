using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Filter;
using E3G.BimTools.DiTools.Core.Models.Parameters;

namespace E3G.BimTools.DiTools.Core.Services.Parameters;

/// <summary>
/// Edicion masiva de un parametro sobre TODA una categoria del modelo (sin
/// filtros).  Sigue el mismo patron que <c>FilterBatchEditService</c> pero
/// recoge la lista de elementos via <c>FilteredElementCollector</c> internamente.
/// </summary>
public sealed class ParameterValueBulkEditService : IParameterValueBulkEditService
{
    private readonly ITransactionService _tx;
    private readonly ILogger _logger;

    public ParameterValueBulkEditService(ITransactionService tx, ILoggerFactory loggerFactory)
    {
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
        _logger = loggerFactory?.CreateLogger<ParameterValueBulkEditService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public BatchEditResult ApplyToCategory(
        Document doc,
        BuiltInCategory category,
        string parameterName,
        string newValue,
        ParameterScope scope)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (string.IsNullOrWhiteSpace(parameterName))
            throw new ArgumentException("Nombre de parametro vacio.", nameof(parameterName));

        var result = new BatchEditResult();
        var instances = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .ToList();

        if (instances.Count == 0)
        {
            result.Warnings.Add($"La categoria '{category}' no tiene elementos en el documento.");
            return result;
        }

        _tx.Run(doc, $"ParaManager: Editar '{parameterName}' en {category}", d =>
        {
            var processedTypeIds = new HashSet<long>();
            foreach (var inst in instances)
            {
                Element? target = inst;
                if (scope == ParameterScope.Type)
                {
                    var typeId = inst.GetTypeId();
                    if (typeId == ElementId.InvalidElementId) { result.Skipped++; continue; }
                    if (!processedTypeIds.Add(typeId.Value)) continue;
                    target = d.GetElement(typeId);
                    if (target is null) { result.Skipped++; continue; }
                }

                if (!TryApply(target!, parameterName, newValue, result))
                    result.Skipped++;
                else
                    result.Modified++;
            }
        });

        _logger.Information("ParaManager BulkEdit '{0}' on {1}: {2}", parameterName, category, result.ToSummary());
        return result;
    }

    private static bool TryApply(Element target, string parameterName, string raw, BatchEditResult result)
    {
        var p = target.LookupParameter(parameterName);
        if (p is null)
        {
            result.Warnings.Add($"Parametro '{parameterName}' no encontrado en {target.Id.Value}.");
            return false;
        }
        if (p.IsReadOnly)
        {
            result.Warnings.Add($"Parametro '{parameterName}' es de solo lectura ({target.Id.Value}).");
            return false;
        }

        try
        {
            return p.StorageType switch
            {
                StorageType.String  => p.Set(raw ?? string.Empty),
                StorageType.Integer => TrySetInt(p, raw, result),
                StorageType.Double  => TrySetDouble(p, raw, result),
                StorageType.ElementId => TrySetElementId(p, raw, result),
                _ => p.Set(raw ?? string.Empty),
            };
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error fijando '{parameterName}' en {target.Id.Value}: {ex.Message}");
            return false;
        }
    }

    private static bool TrySetInt(Parameter p, string raw, BatchEditResult r)
    {
        var clean = (raw ?? string.Empty).Trim();
        if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return p.Set(v);
        if (string.Equals(clean, "true", StringComparison.OrdinalIgnoreCase)) return p.Set(1);
        if (string.Equals(clean, "false", StringComparison.OrdinalIgnoreCase)) return p.Set(0);
        r.Warnings.Add($"Valor entero no valido: '{raw}'.");
        return false;
    }

    private static bool TrySetDouble(Parameter p, string raw, BatchEditResult r)
    {
        var clean = (raw ?? string.Empty).Trim().Replace(',', '.');
        if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return p.Set(v);
        r.Warnings.Add($"Valor decimal no valido: '{raw}'.");
        return false;
    }

    private static bool TrySetElementId(Parameter p, string raw, BatchEditResult r)
    {
        if (long.TryParse((raw ?? string.Empty).Trim(), out var v))
            return p.Set(new ElementId(v));
        r.Warnings.Add($"ElementId no valido: '{raw}'.");
        return false;
    }
}
