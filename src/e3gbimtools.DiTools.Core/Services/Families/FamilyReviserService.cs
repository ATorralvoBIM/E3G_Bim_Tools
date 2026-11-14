using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Families;

namespace E3G.BimTools.DiTools.Core.Services.Families;

/// <summary>
/// Implementación de IFamilyReviserService (Sprint 7 — FamilyReviser).
///
/// Patrón de transacciones:
///   - BulkRename    → una sola Transaction "FamilyReviser: rename".
///   - ExportToDisk  → solo lectura + E/S de disco, sin transacción.
///   - GetFamilies   → solo lectura, sin transacción.
/// </summary>
public sealed class FamilyReviserService : IFamilyReviserService
{
    private readonly ITransactionService _tx;
    private readonly ILogger             _logger;

    public FamilyReviserService(ITransactionService tx, ILoggerFactory loggerFactory)
    {
        _tx     = tx ?? throw new ArgumentNullException(nameof(tx));
        _logger = loggerFactory?.CreateLogger<FamilyReviserService>()
                  ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // ================================================================== //
    //  Lectura                                                             //
    // ================================================================== //

    public IReadOnlyList<FamilyDescriptor> GetFamilies(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var families = new FilteredElementCollector(doc)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .Where(f => !f.IsInPlace)          // excluir in-place
            .OrderBy(f => f.FamilyCategory?.Name ?? string.Empty)
            .ThenBy(f => f.Name)
            .Select(f => BuildDescriptor(doc, f))
            .ToList();

        _logger.Information("FamilyReviserService.GetFamilies: {Count} familias.", families.Count);
        return families;
    }

    // ================================================================== //
    //  Renombrado                                                          //
    // ================================================================== //

    public void PreviewRename(IList<FamilyDescriptor> families, FamilyRenameRequest request)
    {
        if (families is null) throw new ArgumentNullException(nameof(families));
        if (request  is null) throw new ArgumentNullException(nameof(request));

        foreach (var fd in families.Where(f => f.IsSelected))
            fd.ProposedName = request.ComputeProposedName(fd.Name);
    }

    public FamilyRenameResult BulkRename(Document doc, FamilyRenameRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var result  = new FamilyRenameResult();
        var sw      = Stopwatch.StartNew();

        var toRename = request.Families
            .Where(f => f.IsSelected && f.HasRenameChange)
            .ToList();

        if (toRename.Count == 0)
        {
            _logger.Information("FamilyReviserService.BulkRename: no hay familias con cambios de nombre.");
            return result;
        }

        _tx.Run(doc, "FamilyReviser: rename", txDoc =>
        {
            foreach (var fd in toRename)
            {
                if (txDoc.GetElement(fd.FamilyId) is not Family family)
                {
                    result.Skipped.Add((fd.Name, "Elemento no encontrado en el documento."));
                    continue;
                }

                var proposed = fd.ProposedName.Trim();

                if (string.IsNullOrEmpty(proposed))
                {
                    result.Skipped.Add((fd.Name, "El nombre propuesto está vacío."));
                    continue;
                }

                if (ContainsInvalidChars(proposed, out var badChar))
                {
                    result.Skipped.Add((fd.Name, $"Nombre inválido: contiene '{badChar}'."));
                    continue;
                }

                try
                {
                    family.Name = proposed;
                    fd.Name     = proposed;       // reflejar en el descriptor
                    result.RenamedCount++;
                }
                catch (Exception ex)
                {
                    result.Skipped.Add((fd.Name, ex.Message));
                }
            }
        });

        sw.Stop();
        _logger.Information(
            "FamilyReviserService.BulkRename: {Renamed} renombradas, {Skipped} omitidas, {Ms} ms.",
            result.RenamedCount, result.Skipped.Count, sw.ElapsedMilliseconds);

        return result;
    }

    // ================================================================== //
    //  Export                                                              //
    // ================================================================== //

    public FamilyExportResult ExportToDisk(Document doc, FamilyExportRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var result = new FamilyExportResult();

        if (!request.IsValid(out var validationError))
        {
            _logger.Warning("FamilyReviserService.ExportToDisk: petición inválida — {Error}.", validationError ?? string.Empty);
            result.Skipped.Add(("*", validationError ?? "Petición inválida."));
            return result;
        }

        var sw = Stopwatch.StartNew();

        foreach (var fd in request.Families.Where(f => f.IsSelected))
        {
            if (doc.GetElement(fd.FamilyId) is not Family family)
            {
                result.Skipped.Add((fd.Name, "Elemento no encontrado en el documento."));
                continue;
            }

            var relativePath = request.Rule.Resolve(fd);
            var fullPath     = Path.Combine(request.RootFolder, relativePath);
            var dir          = Path.GetDirectoryName(fullPath)!;

            try
            {
                Directory.CreateDirectory(dir);

                if (!request.Overwrite && File.Exists(fullPath))
                {
                    result.Skipped.Add((fd.Name, $"Ya existe en disco: {relativePath}"));
                    continue;
                }

                // SaveAs a una carpeta temporal, luego mover al destino final
                var tempPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(fullPath));

                var familyDoc = doc.EditFamily(family);
                var opts      = new SaveAsOptions { OverwriteExistingFile = true };
                familyDoc.SaveAs(tempPath, opts);
                familyDoc.Close(false);

                File.Copy(tempPath, fullPath, overwrite: true);
                File.Delete(tempPath);

                result.ExportedCount++;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "FamilyReviserService.ExportToDisk: error exportando '{Name}'.", fd.Name);
                result.Skipped.Add((fd.Name, ex.Message));
            }
        }

        sw.Stop();
        _logger.Information(
            "FamilyReviserService.ExportToDisk: {Exported} exportadas, {Skipped} omitidas, {Ms} ms.",
            result.ExportedCount, result.Skipped.Count, sw.ElapsedMilliseconds);

        return result;
    }

    // ================================================================== //
    //  Helpers privados                                                    //
    // ================================================================== //

    private static FamilyDescriptor BuildDescriptor(Document doc, Family family)
    {
        var cat        = family.FamilyCategory;
        var catName    = cat?.Name ?? "Sin categoría";
        var discipline = ResolveDiscipline(cat);
        var typeCount  = family.GetFamilySymbolIds().Count;

        return new FamilyDescriptor
        {
            FamilyId     = family.Id,
            Name         = family.Name,
            ProposedName = family.Name,
            CategoryName = catName,
            Discipline   = discipline,
            TypeCount    = typeCount,
        };
    }

    private static string ResolveDiscipline(Category? cat)
    {
        if (cat is null) return "Genérico";

        // Disciplina heurística basada en categoría built-in
        var id = (BuiltInCategory)cat.Id.IntegerValue;

        if (IsMepCategory(id))         return "MEP";
        if (IsStructuralCategory(id))  return "Estructural";
        if (IsArchitecturalCategory(id)) return "Arquitectura";

        return "Genérico";
    }

    private static bool IsMepCategory(BuiltInCategory id) =>
        id is BuiltInCategory.OST_DuctCurves
          or BuiltInCategory.OST_PipeCurves
          or BuiltInCategory.OST_MechanicalEquipment
          or BuiltInCategory.OST_PlumbingFixtures
          or BuiltInCategory.OST_LightingFixtures
          or BuiltInCategory.OST_ElectricalEquipment
          or BuiltInCategory.OST_ElectricalFixtures
          or BuiltInCategory.OST_FireProtection
          or BuiltInCategory.OST_Sprinklers
          or BuiltInCategory.OST_DuctTerminal
          or BuiltInCategory.OST_DataDevices
          or BuiltInCategory.OST_CommunicationDevices
          or BuiltInCategory.OST_SecurityDevices
          or BuiltInCategory.OST_NurseCallDevices
          or BuiltInCategory.OST_TelephoneDevices;

    private static bool IsStructuralCategory(BuiltInCategory id) =>
        id is BuiltInCategory.OST_StructuralColumns
          or BuiltInCategory.OST_StructuralFraming
          or BuiltInCategory.OST_StructuralFoundation
          or BuiltInCategory.OST_StructuralStiffener
          or BuiltInCategory.OST_Rebar;

    private static bool IsArchitecturalCategory(BuiltInCategory id) =>
        id is BuiltInCategory.OST_Doors
          or BuiltInCategory.OST_Windows
          or BuiltInCategory.OST_Furniture
          or BuiltInCategory.OST_Casework
          or BuiltInCategory.OST_Columns
          or BuiltInCategory.OST_Ceilings
          or BuiltInCategory.OST_Stairs
          or BuiltInCategory.OST_Railings;

    private static bool ContainsInvalidChars(string name, out char badChar)
    {
        // Revit prohíbe: \ / : * ? " < > | { }
        var invalid = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '{', '}' };
        foreach (var c in invalid)
        {
            if (name.Contains(c))
            {
                badChar = c;
                return true;
            }
        }
        badChar = '\0';
        return false;
    }
}
