using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.SheetLink;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Motor SheetLink (Sprint 11-13).
///
/// Intercambio bidireccional entre los planos (ViewSheet) del documento Revit
/// y un archivo Excel (.xlsx), con hoja de metadatos para trazabilidad y
/// re-importacion segura.
///
/// Sprint 11 (P1): Export a .xlsx + Import/Apply desde .xlsx (numero, nombre, revision, firmantes).
/// Sprint 12 (P2): Colocacion y reordenacion de vistas en planos desde el .xlsx.
/// Sprint 13 (P3): Google Sheets + sync bidireccional avanzado.
/// </summary>
public interface ISheetLinkService
{
    // ----------------------------------------------------------------
    // Lectura
    // ----------------------------------------------------------------

    /// <summary>
    /// Lee todos los <see cref="ViewSheet"/> del documento y los devuelve
    /// como lista de <see cref="SheetLinkRow"/> ordenada por SheetNumber.
    /// </summary>
    IReadOnlyList<SheetLinkRow> ReadSheets(Document doc);

    // ----------------------------------------------------------------
    // Exportacion
    // ----------------------------------------------------------------

    /// <summary>
    /// Exporta los planos del documento a un archivo Excel (.xlsx).
    /// El workbook contiene:
    ///   - Hoja "Planos": tabla formateada con cabecera destacada y datos editables.
    ///   - Hoja "_Meta" (oculta): metadatos de exportacion (fecha, documento,
    ///     hash de columnas) para validar el archivo al re-importar en Sprint 12.
    /// </summary>
    SheetLinkExportResult ExportToXlsx(Document doc, SheetLinkExportOptions options);

    // ----------------------------------------------------------------
    // Importacion
    // ----------------------------------------------------------------

    /// <summary>
    /// Lee un .xlsx previamente exportado con <see cref="ExportToXlsx"/> y
    /// devuelve las filas sin aplicar cambios al modelo (solo lectura + validacion).
    /// Permite mostrar un preview al usuario antes de confirmar.
    /// </summary>
    IReadOnlyList<SheetLinkRow> ParseXlsx(string xlsxPath);

    /// <summary>
    /// Aplica los cambios de <paramref name="rows"/> al documento en una unica
    /// transaccion.  Solo actualiza las filas cuyo <c>ElementId</c> coincida con
    /// un plano existente en <paramref name="doc"/>.
    ///
    /// Campos actualizados: SheetNumber, SheetName, RevisionDate, RevisionNumber,
    /// RevisionDescription, IssuedBy, IssuedTo, ApprovedBy, DesignedBy,
    /// CheckedBy, DrawnBy.
    ///
    /// Un unico Ctrl-Z deshace toda la operacion.
    /// </summary>
    SheetLinkImportResult ApplyChanges(Document doc, IReadOnlyList<SheetLinkRow> rows);

    // ----------------------------------------------------------------
    // Sprint 12 — Diff e importacion selectiva con Before/After
    // ----------------------------------------------------------------

    /// <summary>
    /// Valida que el archivo .xlsx fue generado por SheetLink y que su esquema de
    /// columnas es compatible con la version actual de la herramienta.
    ///
    /// Comprueba la presencia de la hoja "Planos" y de la hoja oculta "_Meta", y
    /// que el <c>ColumnHash</c> almacenado en _Meta coincide con el hash del
    /// esquema de columnas actual (detecta archivos de versiones anteriores).
    /// </summary>
    XlsxValidationResult ValidateXlsxSchema(string xlsxPath);

    /// <summary>
    /// Compara los valores del .xlsx editado con los valores actuales del modelo
    /// Revit y devuelve una lista de <see cref="SheetLinkDiffRow"/> con Before/After
    /// por cada plano.
    ///
    /// <list type="bullet">
    ///   <item><description><c>Status = Unchanged</c>: el Excel coincide con el modelo.</description></item>
    ///   <item><description><c>Status = Modified</c>: al menos un campo difiere (IsSelected = true por defecto).</description></item>
    ///   <item><description><c>Status = Missing</c>: el ElementId no existe en el modelo actual (IsSelected = false).</description></item>
    /// </list>
    /// </summary>
    IReadOnlyList<SheetLinkDiffRow> ComputeDiff(Document doc, string xlsxPath);

    /// <summary>
    /// Aplica al modelo unicamente las filas de <paramref name="rows"/> con
    /// <c>IsSelected = true</c> y <c>Status == Modified</c>, dentro de una unica
    /// Transaction nombrada "SheetLink — Aplicar diff".
    ///
    /// Un unico Ctrl-Z deshace toda la operacion. Las filas procesadas reciben
    /// su <c>ApplyStatus</c> actualizado (Updated / Skipped / Error).
    /// </summary>
    SheetLinkImportResult ApplyDiff(Document doc, IReadOnlyList<SheetLinkDiffRow> rows);
}
