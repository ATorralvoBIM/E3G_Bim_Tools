using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Families;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Servicio de gestión de worksets (Sprint 7 — FamilyReviser).
/// Cubre creación, renombrado, eliminación y asignación masiva de elementos.
///
/// Convenio de transacciones:
///   - <see cref="CreateWorkset"/>, <see cref="RenameWorkset"/>, <see cref="DeleteWorkset"/> → Transaction individual.
///   - <see cref="AssignElements"/> → una sola Transaction para todos los elementos.
///   - Métodos Get* → solo lectura, sin transacción.
/// </summary>
public interface IWorksetService
{
    // ------------------------------------------------------------------ //
    //  Lectura                                                             //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Indica si el documento es workshared (central file o local).
    /// Si devuelve <c>false</c> el resto de métodos retornarán vacío o lanzarán.
    /// </summary>
    bool IsWorkshared(Document doc);

    /// <summary>
    /// Devuelve todos los worksets definidos por el usuario, ordenados por nombre.
    /// Incluye el recuento de elementos de cada workset.
    /// </summary>
    IReadOnlyList<WorksetDescriptor> GetWorksets(Document doc);

    // ------------------------------------------------------------------ //
    //  Operaciones CRUD                                                    //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Crea un nuevo workset con el nombre indicado.
    /// Transaction: "FamilyReviser: create workset".
    /// </summary>
    WorksetDescriptor CreateWorkset(Document doc, string name);

    /// <summary>
    /// Crea en lote los worksets indicados (p. ej. importados de un archivo de texto).
    /// Descarta nombres vacíos, duplicados dentro de la lista y los que ya existen en el
    /// documento. Todos los worksets se crean en una sola Transaction.
    /// Transaction: "FamilyReviser: batch create worksets".
    /// </summary>
    WorksetBatchCreateResult CreateWorksets(Document doc, IEnumerable<string> names);

    /// <summary>
    /// Renombra el workset indicado.
    /// Transaction: "FamilyReviser: rename workset".
    /// </summary>
    void RenameWorkset(Document doc, WorksetDescriptor workset, string newName);

    /// <summary>
    /// Renombra en lote todos los worksets de la colección cuyo <see cref="WorksetDescriptor.ProposedName"/>
    /// difiere del nombre actual. Valida nombres vacíos, colisiones y duplicados antes de aplicar.
    /// Todos los renombrados se aplican en una sola Transaction.
    /// Transaction: "FamilyReviser: bulk rename worksets".
    /// </summary>
    WorksetRenameResult BulkRenameWorksets(Document doc, IEnumerable<WorksetDescriptor> worksets);

    /// <summary>
    /// Elimina el workset indicado. Los elementos pasan al workset por defecto.
    /// Transaction: "FamilyReviser: delete workset".
    /// </summary>
    void DeleteWorkset(Document doc, WorksetDescriptor workset);

    // ------------------------------------------------------------------ //
    //  Asignación                                                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Asigna al workset de destino los elementos que cumplen el scope de la petición.
    /// Transaction: "FamilyReviser: assign elements to workset".
    /// </summary>
    WorksetAssignResult AssignElements(Document doc, WorksetAssignRequest request);

    /// <summary>
    /// Devuelve la lista de elementos asignables (familias y/o vínculos Revit/CAD) con su
    /// workset actual, para mostrarlos y reasignarlos desde la pestaña Worksets. Solo lectura.
    /// </summary>
    IReadOnlyList<WorksetAssignableElement> GetAssignableElements(
        Document doc, bool includeFamilies, bool includeRevitLinks, bool includeCadLinks);

    /// <summary>
    /// Consolida todas las rejillas (grids) y niveles del modelo en el workset estándar
    /// "Shared Levels and Grids". Si dicho workset no existe, devuelve un resultado con error.
    /// Transaction: "FamilyReviser: assign elements to workset" (vía AssignElements).
    /// </summary>
    WorksetAssignResult ConsolidateGridsAndLevels(Document doc);

    /// <summary>
    /// Crea un workset por cada vínculo (nombrado a partir del tipo del vínculo) y mueve sus
    /// instancias a él. Las instancias del mismo tipo comparten workset; los worksets ya
    /// existentes se reutilizan. Todo se aplica en una sola Transaction.
    /// Transaction: "FamilyReviser: organize links into worksets".
    /// </summary>
    WorksetLinkOrganizeResult OrganizeLinksIntoWorksets(
        Document doc, bool includeRevitLinks, bool includeCadLinks, string prefix);

    // ------------------------------------------------------------------ //
    //  Visibilidad                                                         //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Muestra u oculta los worksets indicados en el ámbito de vistas seleccionado
    /// (vista activa, todas las vistas, plantillas de vista o ambas). Las vistas que no
    /// soportan visibilidad de workset se omiten. Todo se aplica en una sola Transaction.
    /// Transaction: "FamilyReviser: set workset visibility".
    /// </summary>
    WorksetVisibilityResult SetWorksetVisibility(
        Document doc, IEnumerable<WorksetId> worksetIds, WorksetViewScope scope, bool visible);

    /// <summary>
    /// Genera un reporte de solo lectura del estado de visibilidad (visible/oculto/global) de los
    /// worksets indicados a lo largo del ámbito de vistas elegido. Si <paramref name="worksetIds"/>
    /// está vacío, se reportan todos los worksets de usuario. No abre transacción.
    /// </summary>
    WorksetVisibilityReport GetVisibilityReport(
        Document doc, IEnumerable<WorksetId> worksetIds, WorksetViewScope scope);

    /// <summary>
    /// Crea una vista 3D (isométrica) por cada workset indicado. Cada vista aísla su workset:
    /// ese queda visible y todos los demás ocultos. Las vistas se nombran con
    /// <paramref name="viewNamePrefix"/> + nombre del workset (con sufijo numérico si colisiona).
    /// Todo se crea en una sola Transaction.
    /// Transaction: "FamilyReviser: create 3D views per workset".
    /// </summary>
    WorksetViewCreateResult CreateViewsPerWorkset(
        Document doc, IEnumerable<WorksetId> worksetIds, string viewNamePrefix);

    /// <summary>
    /// Crea UNA sola vista 3D (isométrica) de coordinación con todos los worksets indicados
    /// visibles a la vez y el resto ocultos. La vista se nombra con <paramref name="viewName"/>
    /// (con sufijo numérico si colisiona). Transaction: "FamilyReviser: create coordination view".
    /// </summary>
    WorksetViewCreateResult CreateCoordinationView(
        Document doc, IEnumerable<WorksetId> worksetIds, string viewName);

    // ------------------------------------------------------------------ //
    //  Auditoría                                                           //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Detecta inconsistencias entre el workset actual de cada elemento y el workset
    /// esperado según las reglas por categoría, y worksets sin elementos.
    /// </summary>
    WorksetAuditResult Audit(Document doc);
}
