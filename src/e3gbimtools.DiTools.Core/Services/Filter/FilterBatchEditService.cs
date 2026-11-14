using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Filter;

namespace E3G.BimTools.DiTools.Core.Services.Filter;

/// <summary>
/// Aplica una <see cref="BatchEditOperation"/> a un conjunto de elementos.
/// Toda la operacion va en una unica transaccion via <see cref="ITransactionService"/>
/// para que un solo Ctrl-Z deshaga el cambio completo.
///
/// Si <c>ApplyToType</c>=true, el cambio se aplica al tipo (ElementType) en
/// lugar de a cada instancia.  Cada tipo se modifica una sola vez aunque haya
/// muchas instancias seleccionadas.
/// </summary>
public sealed class FilterBatchEditService : IFilterBatchEditService
{
    private readonly ITransactionService _tx;
    private readonly ILogger _logger;

    public FilterBatchEditService(ITransactionService tx, ILoggerFactory loggerFactory)
    {
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
        _logger = loggerFactory?.CreateLogger<FilterBatchEditService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public BatchEditResult ApplyBatch(Document doc, IReadOnlyList<ElementId> ids, BatchEditOperation op)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (ids is null) throw new ArgumentNullException(nameof(ids));
        if (op is null) throw new ArgumentNullException(nameof(op));
        if (string.IsNullOrWhiteSpace(op.ParameterName))
            throw new ArgumentException("Nombre de parametro vacio.", nameof(op));

        var result = new BatchEditResult();
        if (ids.Count == 0) return result;

        _tx.Run(doc, $"OneFilter: Editar '{op.ParameterName}' en lote", d =>
        {
            // Sets para no procesar dos veces el mismo tipo si varias instancias lo comparten.
            var processedTypeIds = new HashSet<long>();

            foreach (var id in ids)
            {
                var el = d.GetElement(id);
                if (el is null) { result.Skipped++; continue; }

                Element? target = el;
                if (op.ApplyToType)
                {
                    var typeId = el.GetTypeId();
                    if (typeId == ElementId.InvalidElementId)
                    {
                        result.Skipped++;
                        continue;
                    }
                    if (!processedTypeIds.Add(typeId.Value))
                    {
                        // Tipo ya tratado por otra instancia.  No lo contamos como Modified de nuevo.
                        continue;
                    }
                    target = d.GetElement(typeId);
                    if (target is null) { result.Skipped++; continue; }
                }

                if (!TryApply(target!, op, result))
                    result.Skipped++;
                else
                    result.Modified++;
            }
        });

        _logger.Information("OneFilter BatchEdit '{0}': {1}", op.ParameterName, result.ToSummary());
        return result;
    }

    private static bool TryApply(Element target, BatchEditOperation op, BatchEditResult result)
    {
        var p = target.LookupParameter(op.ParameterName);
        if (p is null)
        {
            result.Warnings.Add($"Parametro '{op.ParameterName}' no encontrado en {target.Id.Value}.");
            return false;
        }
        if (p.IsReadOnly)
        {
            result.Warnings.Add($"Parametro '{op.ParameterName}' es de solo lectura ({target.Id.Value}).");
            return false;
        }

        try
        {
            return p.StorageType switch
            {
                StorageType.String  => p.Set(op.NewValue ?? string.Empty),
                StorageType.Integer => TrySetInt(p, op.NewValue, result),
                StorageType.Double  => TrySetDouble(p, op.NewValue, result),
                StorageType.ElementId => TrySetElementId(p, op.NewValue, result),
                _ => p.Set(op.NewValue ?? string.Empty),
            };
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error fijando '{op.ParameterName}' en {target.Id.Value}: {ex.Message}");
            return false;
        }
    }

    private static bool TrySetInt(Parameter p, string raw, BatchEditResult result)
    {
        if (!int.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            // Permitir 0/1 desde "true"/"false" para parametros Yes/No.
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) v = 1;
            else if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) v = 0;
            else { result.Warnings.Add($"Valor entero no valido: '{raw}'."); return false; }
        }
        return p.Set(v);
    }

    private static bool TrySetDouble(Parameter p, string raw, BatchEditResult result)
    {
        var clean = (raw ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            result.Warnings.Add($"Valor decimal no valido: '{raw}'.");
            return false;
        }
        return p.Set(v);
    }

    private static bool TrySetElementId(Parameter p, string raw, BatchEditResult result)
    {
        if (!long.TryParse((raw ?? string.Empty).Trim(), out var v))
        {
            result.Warnings.Add($"ElementId no valido: '{raw}'.");
            return false;
        }
        return p.Set(new ElementId(v));
    }
}
