using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Views;

namespace E3G.BimTools.DiTools.Core.Services.Views;

/// <summary>
/// Implementacion principal del ViewManager (Sprint 4).  Lee, aplica, duplica,
/// renombra y elimina View Templates del documento.
///
/// Decisiones de diseño:
///  - Las operaciones de lectura no abren transaccion.  Filtran por
///    <c>View.IsTemplate</c> sobre el FilteredElementCollector.
///  - Aplicar a N vistas siempre va en UNA sola Transaction nombrada
///    ("ViewManager: aplicar plantilla 'X' a N vistas").  Si una vista
///    individual falla, se registra en <see cref="ViewTemplateApplyResult.FailedViewIds"/>
///    pero la transaccion sigue (vistas restantes pueden completarse).
///  - El export Excel se delega en <see cref="IExcelService"/> existente.
/// </summary>
public sealed class ViewTemplateService : IViewTemplateService
{
    private readonly ITransactionService _tx;
    private readonly IExcelService _excel;
    private readonly ILogger _logger;

    public ViewTemplateService(
        ITransactionService tx,
        IExcelService excel,
        ILoggerFactory loggerFactory)
    {
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
        _excel = excel ?? throw new ArgumentNullException(nameof(excel));
        _logger = loggerFactory?.CreateLogger<ViewTemplateService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // =====================================================================
    // Lectura
    // =====================================================================

    public IReadOnlyList<ViewTemplateInfo> ListAll(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        // Todas las View Templates: View.IsTemplate == true.
        var templates = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .ToList();

        // Indice plantilla -> vistas que la usan.  Se calcula una sola pasada
        // sobre las vistas no-template para no ser O(T*V).
        var usage = new Dictionary<ElementId, List<ElementId>>();
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            if (v.IsTemplate) continue;
            var tplId = v.ViewTemplateId;
            if (tplId is null || tplId == ElementId.InvalidElementId) continue;
            if (!usage.TryGetValue(tplId, out var list))
                usage[tplId] = list = new List<ElementId>();
            list.Add(v.Id);
        }

        return templates
            .Select(t => new ViewTemplateInfo(
                t.Id,
                t.Name,
                t.ViewType,
                usage.TryGetValue(t.Id, out var ids) ? ids : new List<ElementId>()))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ViewTemplateInfo> ListOrphans(Document doc)
        => ListAll(doc).Where(t => t.IsOrphan).ToList();

    public IReadOnlyList<View> ListApplicableViews(Document doc, ViewType templateType)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        return new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate && v.ViewType == templateType)
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // =====================================================================
    // Escritura
    // =====================================================================

    public ViewTemplateApplyResult ApplyTemplate(
        Document doc,
        ElementId templateId,
        IReadOnlyList<ElementId> targetViewIds)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (templateId is null) throw new ArgumentNullException(nameof(templateId));
        if (targetViewIds is null || targetViewIds.Count == 0)
            return new ViewTemplateApplyResult
            {
                Success = false,
                Summary = "Selecciona al menos una vista destino."
            };

        var template = doc.GetElement(templateId) as View;
        if (template is null || !template.IsTemplate)
            return new ViewTemplateApplyResult
            {
                Success = false,
                Summary = "El elemento indicado no es una View Template."
            };

        var result = new ViewTemplateApplyResult();
        _tx.Run(doc, $"ViewManager: aplicar '{template.Name}' a {targetViewIds.Count} vistas", d =>
        {
            foreach (var viewId in targetViewIds)
            {
                try
                {
                    if (d.GetElement(viewId) is not View v || v.IsTemplate)
                    {
                        result.SkippedCount++;
                        continue;
                    }
                    v.ViewTemplateId = templateId;
                    result.AppliedCount++;
                }
                catch (Exception ex)
                {
                    _logger.Warning("Fallo al aplicar plantilla a vista {0}: {1}", viewId, ex.Message);
                    result.FailedCount++;
                    result.FailedViewIds.Add(viewId);
                    result.Warnings.Add($"Vista {viewId}: {ex.Message}");
                }
            }
        });

        result.Success = result.FailedCount == 0;
        result.Summary = $"Plantilla '{template.Name}': {result.AppliedCount} aplicadas, " +
                         $"{result.SkippedCount} omitidas, {result.FailedCount} con error.";
        return result;
    }

    public ViewTemplateApplyResult Duplicate(Document doc, ElementId templateId, string newName)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (string.IsNullOrWhiteSpace(newName))
            return new ViewTemplateApplyResult { Success = false, Summary = "Introduce un nombre para la copia." };

        var template = doc.GetElement(templateId) as View;
        if (template is null || !template.IsTemplate)
            return new ViewTemplateApplyResult { Success = false, Summary = "El elemento indicado no es una View Template." };

        var result = new ViewTemplateApplyResult();
        _tx.Run(doc, $"ViewManager: duplicar plantilla '{template.Name}'", d =>
        {
            try
            {
                // Copiar el elemento plantilla dentro del mismo documento crea otra
                // View Template (conserva IsTemplate=true y todos los ajustes V/G).
                var copiados = ElementTransformUtils.CopyElements(
                    d, new List<ElementId> { templateId }, XYZ.Zero);

                if (copiados.Count == 0 || d.GetElement(copiados.First()) is not View copia)
                {
                    result.Success = false;
                    result.Summary = "No se pudo duplicar la plantilla.";
                    return;
                }

                try { copia.Name = EnsureUniqueViewName(d, newName.Trim()); } catch { /* deja el auto-nombre */ }
                result.Success = true;
                result.AppliedCount = 1;
                result.Summary = $"Plantilla duplicada como '{copia.Name}'.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo al duplicar plantilla");
                result.Success = false;
                result.FailedCount = 1;
                result.Summary = ex.Message;
            }
        });
        return result;
    }

    public ViewTemplateApplyResult Rename(Document doc, ElementId templateId, string newName)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (string.IsNullOrWhiteSpace(newName))
            return new ViewTemplateApplyResult { Success = false, Summary = "Introduce un nombre." };

        var template = doc.GetElement(templateId) as View;
        if (template is null || !template.IsTemplate)
            return new ViewTemplateApplyResult { Success = false, Summary = "El elemento no es una View Template." };

        var result = new ViewTemplateApplyResult();
        _tx.Run(doc, $"ViewManager: renombrar plantilla a '{newName}'", d =>
        {
            try
            {
                template.Name = newName;
                result.Success = true;
                result.AppliedCount = 1;
                result.Summary = $"Plantilla renombrada a '{newName}'.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo al renombrar plantilla");
                result.Success = false;
                result.FailedCount = 1;
                result.Summary = ex.Message;
            }
        });
        return result;
    }

    public ViewTemplateApplyResult Delete(Document doc, ElementId templateId, bool forceUnassign)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var template = doc.GetElement(templateId) as View;
        if (template is null || !template.IsTemplate)
            return new ViewTemplateApplyResult { Success = false, Summary = "El elemento indicado no es una View Template." };

        // Vistas que la usan actualmente.
        var enUso = new FilteredElementCollector(doc)
            .OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && v.ViewTemplateId == templateId)
            .ToList();

        if (enUso.Count > 0 && !forceUnassign)
        {
            var muestra = string.Join(", ", enUso.Take(5).Select(v => v.Name));
            if (enUso.Count > 5) muestra += $"… (+{enUso.Count - 5})";
            return new ViewTemplateApplyResult
            {
                Success = false,
                Summary = $"La plantilla está en uso por {enUso.Count} vista(s): {muestra}. " +
                          "Confirma para desasignarla de esas vistas y eliminarla.",
            };
        }

        string nombre = template.Name;
        var result = new ViewTemplateApplyResult();
        _tx.Run(doc, $"ViewManager: eliminar plantilla '{nombre}'", d =>
        {
            try
            {
                if (forceUnassign)
                {
                    foreach (var v in enUso)
                    {
                        try { v.ViewTemplateId = ElementId.InvalidElementId; }
                        catch (Exception ex) { result.Warnings.Add($"Vista '{v.Name}': {ex.Message}"); }
                    }
                }
                d.Delete(templateId);
                result.Success = true;
                result.AppliedCount = 1;
                result.Summary = $"Plantilla '{nombre}' eliminada" +
                                 (enUso.Count > 0 ? $" (desasignada de {enUso.Count} vista(s))." : ".");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo al eliminar plantilla");
                result.Success = false;
                result.FailedCount = 1;
                result.Summary = ex.Message;
            }
        });
        return result;
    }

    // =====================================================================
    // Export
    // =====================================================================

    public string ExportMatrixToExcel(Document doc, string outputPath)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Ruta de salida vacía.", nameof(outputPath));

        var nombrePorPlantilla = new FilteredElementCollector(doc)
            .OfClass(typeof(View)).Cast<View>()
            .Where(v => v.IsTemplate)
            .ToDictionary(v => v.Id, v => v.Name);

        var filas = new FilteredElementCollector(doc)
            .OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate)
            .OrderBy(v => v.ViewType.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => new MatrizFila
            {
                Vista = v.Name,
                Tipo = v.ViewType.ToString(),
                PlantillaAsignada = v.ViewTemplateId != ElementId.InvalidElementId &&
                                    nombrePorPlantilla.TryGetValue(v.ViewTemplateId, out var n) ? n : "(ninguna)",
            })
            .ToList();

        _excel.WriteSheet(outputPath, "Vistas x Plantilla", filas);
        _logger.Information("ViewManager: matriz exportada a '{0}' ({1} vistas).", outputPath, filas.Count);
        return outputPath;
    }

    /// <summary>Fila de la matriz Vista × Plantilla para el export Excel.</summary>
    public sealed class MatrizFila
    {
        public string Vista { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string PlantillaAsignada { get; set; } = string.Empty;
    }

    private static string EnsureUniqueViewName(Document doc, string name)
    {
        var usados = new HashSet<string>(
            new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
            StringComparer.OrdinalIgnoreCase);
        if (!usados.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            var cand = $"{name} ({i})";
            if (!usados.Contains(cand)) return cand;
        }
    }
}
