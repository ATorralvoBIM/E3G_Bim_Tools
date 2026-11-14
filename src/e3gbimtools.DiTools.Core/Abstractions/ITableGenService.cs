using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.TableGen;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Servicio TableGen.
/// Sprint 8 — P1: lectura de Excel y generacion de Drafting Views.
/// Sprint 9 — P2: generacion en lote, sobreescritura, colocacion en hoja
///              y exportacion de Revit Schedules a Drafting View.
/// </summary>
public interface ITableGenService
{
    // ── Sprint 8 (P1) ────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve la lista de hojas del archivo .xlsx indicado.
    /// No necesita contexto de Revit.
    /// </summary>
    IReadOnlyList<string> GetSheetNames(string filePath);

    /// <summary>
    /// Devuelve la direccion del rango utilizado de una hoja
    /// (p. ej. "A1:J20").  No necesita contexto de Revit.
    /// </summary>
    string GetUsedRangeAddress(string filePath, string sheetName);

    /// <summary>
    /// Lee el rango indicado y devuelve la estructura de celdas lista
    /// para mostrar en la UI de preview.  No necesita contexto de Revit.
    /// </summary>
    ExcelSheetData ReadPreview(TableGenRequest request);

    /// <summary>
    /// Genera una nueva Drafting View en <paramref name="doc"/> a partir
    /// del Excel y los parametros del request.
    /// Si <see cref="TableGenRequest.OverwriteExisting"/> es true y existe
    /// una vista con el mismo nombre, la elimina antes de crear la nueva.
    /// Debe llamarse desde el hilo de Revit con el documento activo.
    /// </summary>
    TableGenResult Generate(Document doc, TableGenRequest request);

    // ── Sprint 9 (P2) — Consultas sin transaccion ─────────────────────

    /// <summary>
    /// Devuelve los ViewSchedules del documento, ordenados por nombre.
    /// </summary>
    IReadOnlyList<ScheduleInfo> GetSchedules(Document doc);

    /// <summary>
    /// Devuelve los Drawing Sheets (ViewSheet) del documento,
    /// ordenados por numero de hoja.
    /// </summary>
    IReadOnlyList<SheetPickerInfo> GetDrawingSheets(Document doc);

    /// <summary>
    /// Devuelve las Drafting Views del documento, indicando si ya estan
    /// colocadas en alguna hoja.
    /// </summary>
    IReadOnlyList<DraftingViewInfo> GetDraftingViews(Document doc);

    // ── Sprint 9 (P2) — Operaciones con transaccion ───────────────────

    /// <summary>
    /// Genera una Drafting View por cada hoja del archivo Excel indicado,
    /// en una sola llamada (lote).
    /// </summary>
    BatchTableGenResult GenerateBatch(Document doc, BatchTableGenRequest request);

    /// <summary>
    /// Coloca una Drafting View existente sobre un Drawing Sheet.
    /// Si la vista ya tiene un Viewport en esa hoja, lo elimina y lo
    /// recoloca en la nueva posicion.
    /// </summary>
    PlaceOnSheetResult PlaceOnSheet(Document doc, PlaceOnSheetRequest request);

    /// <summary>
    /// Lee un ViewSchedule del documento y genera una Drafting View con
    /// su contenido, usando el mismo generador de DraftingViewGenerator
    /// que la ruta Excel → Vista.
    /// </summary>
    ScheduleExportResult ExportScheduleToView(Document doc, ScheduleToViewRequest request);
}
