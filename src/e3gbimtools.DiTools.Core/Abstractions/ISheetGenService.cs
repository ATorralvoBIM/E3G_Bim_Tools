using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Sheets;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Servicio principal de SheetGen (Sprint 6).
/// Agrupa creación masiva, import/export Excel, layouts, revisiones y auditoría.
/// </summary>
public interface ISheetGenService
{
    // ------------------------------------------------------------------ //
    //  Creación / Import / Export                                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Crea múltiples Drawing Sheets en una sola transacción.
    /// Aplica el <paramref name="layout"/> si se proporciona.
    /// Informa progreso via <paramref name="progress"/> (valor 0-100).
    /// </summary>
    Task<SheetGenResult> BulkCreateAsync(
        Document doc,
        IList<SheetDescriptor> descriptors,
        SheetLayoutDefinition? layout,
        IProgress<int>? progress);

    /// <summary>
    /// Lee un .xlsx con columnas SheetNumber, SheetName, TitleBlockFamilyTypeName,
    /// Discipline y crea las sheets. Valida duplicados antes de abrir la transacción.
    /// </summary>
    SheetGenResult ImportFromExcel(
        Document doc,
        string filePath,
        SheetLayoutDefinition? layout);

    /// <summary>
    /// Exporta todas las Drawing Sheets del documento a un .xlsx con columnas
    /// SheetNumber, SheetName, TitleBlockFamilyTypeName, Discipline, ViewCount.
    /// </summary>
    void ExportToExcel(Document doc, string filePath);

    // ------------------------------------------------------------------ //
    //  Revisiones                                                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Asigna o quita la revisión indicada a un conjunto de sheets
    /// en una sola transacción.
    /// </summary>
    void ApplyRevisions(Document doc, SheetRevisionRequest request);

    // ------------------------------------------------------------------ //
    //  Renumeración                                                        //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Renumera las sheets en el orden indicado siguiendo el patrón del
    /// <paramref name="request"/>. Revit actualiza automáticamente las
    /// referencias (Drawing List Schedules, tagged elements).
    /// </summary>
    void RenumberSheets(Document doc, SheetRenumberRequest request);

    /// <summary>
    /// Previsualiza la renumeración: devuelve pares (SheetId, NúmeroAnterior, NúmeroNuevo)
    /// sin tocar el documento.
    /// </summary>
    IList<(ElementId SheetId, string OldNumber, string NewNumber)> PreviewRenumber(
        Document doc,
        SheetRenumberRequest request);

    // ------------------------------------------------------------------ //
    //  Auditoría                                                           //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Analiza el documento y devuelve sheets huérfanas (0 viewports)
    /// y números duplicados.
    /// </summary>
    SheetAuditResult Audit(Document doc);

    /// <summary>Devuelve info de todas las Drawing Sheets del documento.</summary>
    IList<SheetInfo> GetAllSheets(Document doc);

    // ------------------------------------------------------------------ //
    //  Layouts                                                             //
    // ------------------------------------------------------------------ //

    /// <summary>Devuelve todos los layouts guardados.</summary>
    IList<SheetLayoutDefinition> GetLayouts();

    /// <summary>Guarda (o sobreescribe) un layout por su <see cref="SheetLayoutDefinition.Id"/>.</summary>
    void SaveLayout(SheetLayoutDefinition layout);

    /// <summary>Elimina el layout con el id indicado (no-op si no existe).</summary>
    void DeleteLayout(Guid layoutId);
}
