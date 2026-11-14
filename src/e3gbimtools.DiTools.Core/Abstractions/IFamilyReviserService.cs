using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Families;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Servicio principal de FamilyReviser (Sprint 7).
/// Cubre renombrado masivo de familias y export organizado a disco.
///
/// Convenio de transacciones:
///   - <see cref="BulkRename"/>    → abre una sola Transaction.
///   - <see cref="ExportToDisk"/>  → solo lectura + E/S de disco, sin transacción.
///   - <see cref="GetFamilies"/>   → solo lectura, sin transacción.
/// </summary>
public interface IFamilyReviserService
{
    // ------------------------------------------------------------------ //
    //  Lectura                                                             //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Devuelve todas las familias del documento ordenadas por categoría y nombre.
    /// Incluye familia, número de tipos y la disciplina derivada.
    /// </summary>
    IReadOnlyList<FamilyDescriptor> GetFamilies(Document doc);

    // ------------------------------------------------------------------ //
    //  Renombrado                                                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Aplica en preview las reglas de la petición sobre cada familia, actualizando
    /// <see cref="FamilyDescriptor.ProposedName"/> sin tocar el documento.
    /// </summary>
    void PreviewRename(IList<FamilyDescriptor> families, FamilyRenameRequest request);

    /// <summary>
    /// Renombra las familias seleccionadas según <see cref="FamilyDescriptor.ProposedName"/>
    /// en una sola transacción nombrada "FamilyReviser: rename".
    /// </summary>
    FamilyRenameResult BulkRename(Document doc, FamilyRenameRequest request);

    // ------------------------------------------------------------------ //
    //  Export                                                              //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Exporta los archivos .rfa de las familias indicadas a disco,
    /// organizándolos según la <see cref="FamilyExportRule"/> de la petición.
    /// </summary>
    FamilyExportResult ExportToDisk(Document doc, FamilyExportRequest request);
}
