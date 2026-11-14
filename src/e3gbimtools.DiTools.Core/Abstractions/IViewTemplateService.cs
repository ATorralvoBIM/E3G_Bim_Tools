using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Views;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Gestion centralizada de View Templates (Sprint 4 - ViewManager).
/// Operaciones de lectura no abren transaccion; las de escritura agrupan
/// todo en una unica <c>ITransactionService.Run</c>.
/// </summary>
public interface IViewTemplateService
{
    // ---- Lectura ----

    /// <summary>Listado de todas las View Templates del documento.</summary>
    IReadOnlyList<ViewTemplateInfo> ListAll(Document doc);

    /// <summary>Plantillas sin vistas asociadas.</summary>
    IReadOnlyList<ViewTemplateInfo> ListOrphans(Document doc);

    /// <summary>Lista vistas del documento que pueden recibir una plantilla.</summary>
    IReadOnlyList<View> ListApplicableViews(Document doc, ViewType templateType);

    // ---- Escritura ----

    /// <summary>Aplica una plantilla a N vistas en una unica Transaction.</summary>
    ViewTemplateApplyResult ApplyTemplate(
        Document doc,
        ElementId templateId,
        IReadOnlyList<ElementId> targetViewIds);

    /// <summary>Duplica una plantilla con nombre nuevo.</summary>
    ViewTemplateApplyResult Duplicate(Document doc, ElementId templateId, string newName);

    /// <summary>Renombra una plantilla (valida unicidad).</summary>
    ViewTemplateApplyResult Rename(Document doc, ElementId templateId, string newName);

    /// <summary>Elimina una plantilla. Si esta en uso devuelve error con la lista de vistas.</summary>
    ViewTemplateApplyResult Delete(Document doc, ElementId templateId, bool forceUnassign);

    // ---- Export ----

    /// <summary>Exporta a Excel la matriz Vista x Template del documento.</summary>
    string ExportMatrixToExcel(Document doc, string outputPath);
}
