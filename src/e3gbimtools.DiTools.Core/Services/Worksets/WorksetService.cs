using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Families;

namespace E3G.BimTools.DiTools.Core.Services.Worksets;

/// <summary>
/// Implementación de IWorksetService (Sprint 7 — FamilyReviser).
///
/// Patrón de transacciones:
///   - CreateWorkset, RenameWorkset, DeleteWorkset → Transaction individual.
///   - AssignElements → una sola Transaction para todos los elementos.
///   - Métodos Get*, IsWorkshared, Audit → solo lectura, sin transacción.
/// </summary>
public sealed class WorksetService : IWorksetService
{
    private readonly ITransactionService _tx;
    private readonly ILogger             _logger;

    public WorksetService(ITransactionService tx, ILoggerFactory loggerFactory)
    {
        _tx     = tx ?? throw new ArgumentNullException(nameof(tx));
        _logger = loggerFactory?.CreateLogger<WorksetService>()
                  ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // ================================================================== //
    //  Lectura                                                             //
    // ================================================================== //

    public bool IsWorkshared(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        return doc.IsWorkshared;
    }

    public IReadOnlyList<WorksetDescriptor> GetWorksets(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.GetWorksets: el documento no es workshared.");
            return Array.Empty<WorksetDescriptor>();
        }

        // Precalcular elementos por workset
        var countByWorkset = BuildElementCountByWorkset(doc);

        var result = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .OrderBy(w => w.Name)
            .Select(w =>
            {
                countByWorkset.TryGetValue(w.Id, out var count);
                return new WorksetDescriptor
                {
                    Id           = w.Id,
                    Name         = w.Name,
                    ProposedName = w.Name,
                    ElementCount = count,
                };
            })
            .ToList();

        _logger.Information("WorksetService.GetWorksets: {Count} worksets.", result.Count);
        return result;
    }

    // ================================================================== //
    //  CRUD                                                                //
    // ================================================================== //

    public WorksetDescriptor CreateWorkset(Document doc, string name)
    {
        if (doc is null)                  throw new ArgumentNullException(nameof(doc));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El nombre no puede estar vacío.", nameof(name));

        WorksetDescriptor? descriptor = null;

        _tx.Run(doc, "FamilyReviser: create workset", txDoc =>
        {
            var workset = Workset.Create(txDoc, name);
            descriptor  = new WorksetDescriptor
            {
                Id           = workset.Id,
                Name         = workset.Name,
                ProposedName = workset.Name,
                ElementCount = 0,
            };
        });

        _logger.Information("WorksetService.CreateWorkset: creado '{Name}'.", name);
        return descriptor!;
    }

    public WorksetBatchCreateResult CreateWorksets(Document doc, IEnumerable<string> names)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new WorksetBatchCreateResult();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.CreateWorksets: el documento no es workshared.");
            return result;
        }

        var existingNames = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .Select(w => w.Name)
            .ToList();

        var toCreate = result.Plan(names, existingNames);

        if (toCreate.Count == 0)
        {
            _logger.Information(
                "WorksetService.CreateWorksets: nada que crear ({Existing} ya existían, {Dups} duplicados).",
                result.AlreadyExisting.Count, result.DuplicateInInput.Count);
            return result;
        }

        _tx.Run(doc, "FamilyReviser: batch create worksets", txDoc =>
        {
            foreach (var name in toCreate)
            {
                try
                {
                    Workset.Create(txDoc, name);
                    result.Created.Add(name);
                }
                catch (Exception ex)
                {
                    result.Errors.Add((name, ex.Message));
                }
            }
        });

        _logger.Information(
            "WorksetService.CreateWorksets: {Created} creados, {Existing} ya existían, {Dups} duplicados, {Errors} errores.",
            result.Created.Count, result.AlreadyExisting.Count, result.DuplicateInInput.Count, result.Errors.Count);

        return result;
    }

    public void RenameWorkset(Document doc, WorksetDescriptor workset, string newName)
    {
        if (doc      is null) throw new ArgumentNullException(nameof(doc));
        if (workset  is null) throw new ArgumentNullException(nameof(workset));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("El nombre no puede estar vacío.", nameof(newName));

        _tx.Run(doc, "FamilyReviser: rename workset", txDoc =>
        {
            WorksetTable.RenameWorkset(txDoc, workset.Id, newName);
            workset.Name         = newName;
            workset.ProposedName = newName;
        });

        _logger.Information("WorksetService.RenameWorkset: renombrado a '{NewName}'.", newName);
    }

    public WorksetRenameResult BulkRenameWorksets(Document doc, IEnumerable<WorksetDescriptor> worksets)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new WorksetRenameResult();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.BulkRenameWorksets: el documento no es workshared.");
            return result;
        }

        var list = (worksets ?? Enumerable.Empty<WorksetDescriptor>()).ToList();

        var changes = list
            .Where(w => w.HasRenameChange)
            .Select(w => (w.Name, w.ProposedName))
            .ToList();

        if (changes.Count == 0)
        {
            _logger.Information("WorksetService.BulkRenameWorksets: no hay cambios de nombre pendientes.");
            return result;
        }

        var allCurrentNames = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .Select(w => w.Name)
            .ToList();

        var valid = result.Plan(changes, allCurrentNames);

        if (valid.Count == 0)
        {
            _logger.Information("WorksetService.BulkRenameWorksets: ningún cambio válido tras la validación.");
            return result;
        }

        // Workset names son únicos en Revit → clave por nombre actual.
        var byName = list.ToDictionary(w => w.Name, w => w, StringComparer.OrdinalIgnoreCase);

        _tx.Run(doc, "FamilyReviser: bulk rename worksets", txDoc =>
        {
            foreach (var (orig, proposed) in valid)
            {
                if (!byName.TryGetValue(orig, out var descriptor)) continue;

                try
                {
                    WorksetTable.RenameWorkset(txDoc, descriptor.Id, proposed);
                    descriptor.Name         = proposed;
                    descriptor.ProposedName = proposed;
                    result.RenamedCount++;
                }
                catch (Exception ex)
                {
                    result.Skipped.Add((orig, proposed, ex.Message));
                }
            }
        });

        _logger.Information(
            "WorksetService.BulkRenameWorksets: {Renamed} renombrados, {Skipped} omitidos.",
            result.RenamedCount, result.Skipped.Count);

        return result;
    }

    public void DeleteWorkset(Document doc, WorksetDescriptor workset)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (workset is null) throw new ArgumentNullException(nameof(workset));

        _tx.Run(doc, "FamilyReviser: delete workset", txDoc =>
        {
            // Los elementos pasan al workset activo por defecto
            var defaultId = txDoc.GetWorksetTable().GetActiveWorksetId();
            // DeleteWorksetSettings(DeleteWorksetOption, WorksetId) — el orden importa
            WorksetTable.DeleteWorkset(txDoc, workset.Id, new DeleteWorksetSettings(DeleteWorksetOption.MoveElementsToWorkset, defaultId));
        });

        _logger.Information("WorksetService.DeleteWorkset: eliminado '{Name}'.", workset.Name);
    }

    // ================================================================== //
    //  Asignación                                                          //
    // ================================================================== //

    public WorksetAssignResult AssignElements(Document doc, WorksetAssignRequest request)
    {
        if (doc     is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var result = new WorksetAssignResult();

        if (!request.IsValid(out var validationError))
        {
            _logger.Warning("WorksetService.AssignElements: petición inválida — {Error}.", validationError ?? string.Empty);
            return result;
        }

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.AssignElements: el documento no es workshared.");
            return result;
        }

        // Resolver scope de elementos
        var elementIds = ResolveScope(doc, request);

        if (elementIds.Count == 0)
        {
            _logger.Information("WorksetService.AssignElements: scope vacío.");
            return result;
        }

        var sw = Stopwatch.StartNew();

        _tx.Run(doc, "FamilyReviser: assign elements to workset", txDoc =>
        {
            var worksetParam = new BuiltInParameter[] { BuiltInParameter.ELEM_PARTITION_PARAM };

            foreach (var id in elementIds)
            {
                var elem = txDoc.GetElement(id);
                if (elem is null) continue;

                var param = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (param is null || param.IsReadOnly) continue;

                var currentWorksetId = new WorksetId(param.AsInteger());
                if (currentWorksetId == request.TargetWorksetId)
                {
                    result.AlreadyInTargetCount++;
                    continue;
                }

                try
                {
                    param.Set(request.TargetWorksetId.IntegerValue);
                    result.AssignedCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add((id.IntegerValue, ex.Message));
                }
            }
        });

        sw.Stop();
        _logger.Information(
            "WorksetService.AssignElements: {Assigned} asignados, {Already} ya estaban, {Errors} errores, {Ms} ms.",
            result.AssignedCount, result.AlreadyInTargetCount, result.Errors.Count, sw.ElapsedMilliseconds);

        return result;
    }

    public IReadOnlyList<WorksetAssignableElement> GetAssignableElements(
        Document doc, bool includeFamilies, bool includeRevitLinks, bool includeCadLinks)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.GetAssignableElements: el documento no es workshared.");
            return Array.Empty<WorksetAssignableElement>();
        }

        var nameByWorkset = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .ToDictionary(w => w.Id, w => w.Name);

        string WorksetNameOf(Element e)
        {
            var p = e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (p is null) return "—";
            return nameByWorkset.TryGetValue(new WorksetId(p.AsInteger()), out var n) ? n : "—";
        }

        // Tipo de sistema MEP, si el elemento lo tuviera (conductos, tuberías, etc.).
        static string SystemTypeOf(Element e)
        {
            var bips = new[]
            {
                BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM,
                BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM,
                BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM,
            };
            foreach (var bip in bips)
            {
                Parameter p;
                try { p = e.get_Parameter(bip); }
                catch { p = null; }
                var s = p?.AsValueString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return string.Empty;
        }

        var rows = new List<WorksetAssignableElement>();

        // Familias = instancias de familias de componente colocadas en el modelo.
        if (includeFamilies)
        {
            foreach (var inst in new FilteredElementCollector(doc)
                         .OfClass(typeof(FamilyInstance))
                         .WhereElementIsNotElementType()
                         .Cast<FamilyInstance>())
            {
                var familyName = inst.Symbol?.FamilyName ?? inst.Name;
                var typeName   = inst.Name;
                rows.Add(new WorksetAssignableElement
                {
                    Id             = inst.Id,
                    Kind           = "Familia",
                    Name           = string.IsNullOrWhiteSpace(typeName) ? familyName : $"{familyName} : {typeName}",
                    Category       = inst.Category?.Name ?? string.Empty,
                    SystemType     = SystemTypeOf(inst),
                    CurrentWorkset = WorksetNameOf(inst),
                });
            }
        }

        if (includeRevitLinks)
        {
            foreach (var inst in new FilteredElementCollector(doc)
                         .OfClass(typeof(RevitLinkInstance))
                         .WhereElementIsNotElementType())
            {
                rows.Add(new WorksetAssignableElement
                {
                    Id             = inst.Id,
                    Kind           = "Vínculo Revit",
                    Name           = inst.Name,
                    Category       = inst.Category?.Name ?? "Vínculos RVT",
                    CurrentWorkset = WorksetNameOf(inst),
                });
            }
        }

        if (includeCadLinks)
        {
            foreach (var inst in new FilteredElementCollector(doc)
                         .OfClass(typeof(ImportInstance))
                         .WhereElementIsNotElementType())
            {
                var typeName = doc.GetElement(inst.GetTypeId())?.Name;
                rows.Add(new WorksetAssignableElement
                {
                    Id             = inst.Id,
                    Kind           = "Vínculo CAD",
                    Name           = string.IsNullOrWhiteSpace(typeName) ? inst.Name : typeName,
                    Category       = inst.Category?.Name ?? "Importación CAD",
                    CurrentWorkset = WorksetNameOf(inst),
                });
            }
        }

        var ordered = rows
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.Category)
            .ThenBy(r => r.Name)
            .ToList();

        _logger.Information("WorksetService.GetAssignableElements: {Count} elementos.", ordered.Count);
        return ordered;
    }

    /// <summary>Nombre del workset estándar de Revit para rejillas y niveles compartidos.</summary>
    public const string SharedLevelsAndGridsWorksetName = "Shared Levels and Grids";

    public WorksetAssignResult ConsolidateGridsAndLevels(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new WorksetAssignResult();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.ConsolidateGridsAndLevels: el documento no es workshared.");
            return result;
        }

        var target = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .FirstOrDefault(w => string.Equals(w.Name, SharedLevelsAndGridsWorksetName, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            _logger.Warning("WorksetService.ConsolidateGridsAndLevels: no existe el workset '{Name}'.",
                SharedLevelsAndGridsWorksetName);
            result.Errors.Add((0, $"No se encontró el workset '{SharedLevelsAndGridsWorksetName}'."));
            return result;
        }

        var request = new WorksetAssignRequest
        {
            TargetWorksetId = target.Id,
            CategoryFilter  = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Grids,
                BuiltInCategory.OST_Levels,
            },
        };

        return AssignElements(doc, request);
    }

    public WorksetLinkOrganizeResult OrganizeLinksIntoWorksets(
        Document doc, bool includeRevitLinks, bool includeCadLinks, string prefix)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new WorksetLinkOrganizeResult();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.OrganizeLinksIntoWorksets: el documento no es workshared.");
            return result;
        }

        var instances = new List<Element>();
        if (includeRevitLinks)
            instances.AddRange(new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .WhereElementIsNotElementType());
        if (includeCadLinks)
            instances.AddRange(new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType());

        if (instances.Count == 0)
        {
            _logger.Information("WorksetService.OrganizeLinksIntoWorksets: no hay vínculos en el scope.");
            return result;
        }

        // Worksets existentes por nombre (clave insensible a mayúsculas).
        var existing = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // Agrupar instancias por nombre de workset destino (todas las del mismo tipo juntas).
        var groups = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
        foreach (var inst in instances)
        {
            var typeName = doc.GetElement(inst.GetTypeId())?.Name ?? inst.Name;
            var wsName   = WorksetLinkOrganizeResult.SanitizeWorksetName(prefix, typeName);

            if (!groups.TryGetValue(wsName, out var list))
            {
                list = new List<ElementId>();
                groups[wsName] = list;
            }
            list.Add(inst.Id);
        }

        _tx.Run(doc, "FamilyReviser: organize links into worksets", txDoc =>
        {
            foreach (var kvp in groups)
            {
                var wsName = kvp.Key;
                WorksetId targetId;

                if (existing.TryGetValue(wsName, out var existingId))
                {
                    targetId = existingId;
                    result.WorksetsReused.Add(wsName);
                }
                else
                {
                    try
                    {
                        var ws = Workset.Create(txDoc, wsName);
                        targetId = ws.Id;
                        existing[wsName] = targetId;
                        result.WorksetsCreated.Add(wsName);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add((wsName, ex.Message));
                        continue;
                    }
                }

                foreach (var id in kvp.Value)
                {
                    var elem  = txDoc.GetElement(id);
                    var param = elem?.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (param is null || param.IsReadOnly) continue;
                    if (new WorksetId(param.AsInteger()) == targetId) continue;

                    try
                    {
                        param.Set(targetId.IntegerValue);
                        result.InstancesAssigned++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add((wsName, ex.Message));
                    }
                }
            }
        });

        _logger.Information(
            "WorksetService.OrganizeLinksIntoWorksets: {Created} creados, {Reused} reutilizados, {Assigned} instancias asignadas, {Errors} errores.",
            result.WorksetsCreated.Count, result.WorksetsReused.Count, result.InstancesAssigned, result.Errors.Count);

        return result;
    }

    // ================================================================== //
    //  Visibilidad                                                         //
    // ================================================================== //

    public WorksetVisibilityResult SetWorksetVisibility(
        Document doc, IEnumerable<WorksetId> worksetIds, WorksetViewScope scope, bool visible)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new WorksetVisibilityResult();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.SetWorksetVisibility: el documento no es workshared.");
            return result;
        }

        var wsIds = (worksetIds ?? Enumerable.Empty<WorksetId>()).Distinct().ToList();
        if (wsIds.Count == 0)
        {
            _logger.Information("WorksetService.SetWorksetVisibility: no se indicaron worksets.");
            return result;
        }

        var views = ResolveViews(doc, scope);
        if (views.Count == 0)
        {
            _logger.Information("WorksetService.SetWorksetVisibility: el ámbito no contiene vistas.");
            return result;
        }

        var target = visible ? WorksetVisibility.Visible : WorksetVisibility.Hidden;

        _tx.Run(doc, "FamilyReviser: set workset visibility", txDoc =>
        {
            foreach (var view in views)
            {
                var supported = false;

                foreach (var wsId in wsIds)
                {
                    try
                    {
                        var current = view.GetWorksetVisibility(wsId);
                        supported = true;

                        if (current == target)
                        {
                            result.AlreadyCount++;
                            continue;
                        }

                        view.SetWorksetVisibility(wsId, target);
                        result.ChangedCount++;
                    }
                    catch (Exception ex)
                    {
                        // La primera excepción de una vista no soportada corta su procesado.
                        if (!supported)
                        {
                            result.SkippedViews++;
                            break;
                        }
                        result.Errors.Add((view.Name, ex.Message));
                    }
                }

                if (supported) result.ViewsAffected++;
            }
        });

        _logger.Information(
            "WorksetService.SetWorksetVisibility: {Views} vistas, {Changed} cambios, {Already} ya estaban, {Skipped} no soportadas, {Errors} errores.",
            result.ViewsAffected, result.ChangedCount, result.AlreadyCount, result.SkippedViews, result.Errors.Count);

        return result;
    }

    public WorksetVisibilityReport GetVisibilityReport(
        Document doc, IEnumerable<WorksetId> worksetIds, WorksetViewScope scope)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var report = new WorksetVisibilityReport();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.GetVisibilityReport: el documento no es workshared.");
            return report;
        }

        var nameById = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .ToDictionary(w => w.Id, w => w.Name);

        var ids = (worksetIds ?? Enumerable.Empty<WorksetId>()).Distinct().ToList();
        if (ids.Count == 0)
            ids = nameById.Keys.ToList();

        if (ids.Count == 0)
            return report;

        var rowById = ids.ToDictionary(
            id => id,
            id => new WorksetVisibilityReportRow
            {
                WorksetName = nameById.TryGetValue(id, out var n) ? n : $"(id {id.IntegerValue})",
            });

        foreach (var view in ResolveViews(doc, scope))
        {
            var supported = false;

            foreach (var id in ids)
            {
                try
                {
                    var visibility = view.GetWorksetVisibility(id);
                    supported = true;

                    var row = rowById[id];
                    switch (visibility)
                    {
                        case WorksetVisibility.Visible:          row.VisibleCount++; break;
                        case WorksetVisibility.Hidden:           row.HiddenCount++;  break;
                        case WorksetVisibility.UseGlobalSetting: row.GlobalCount++;  break;
                    }
                }
                catch
                {
                    if (!supported) break; // vista no compatible
                }
            }

            if (supported) report.ViewsInspected++;
        }

        report.Rows = rowById.Values.OrderBy(r => r.WorksetName).ToList();

        _logger.Information(
            "WorksetService.GetVisibilityReport: {Views} vistas inspeccionadas, {Worksets} worksets.",
            report.ViewsInspected, report.Rows.Count);

        return report;
    }

    public WorksetViewCreateResult CreateViewsPerWorkset(
        Document doc, IEnumerable<WorksetId> worksetIds, string viewNamePrefix)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new WorksetViewCreateResult();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.CreateViewsPerWorkset: el documento no es workshared.");
            return result;
        }

        var targetIds = (worksetIds ?? Enumerable.Empty<WorksetId>()).Distinct().ToList();
        if (targetIds.Count == 0)
        {
            _logger.Information("WorksetService.CreateViewsPerWorkset: no se indicaron worksets.");
            return result;
        }

        // Todos los worksets de usuario (para nombrar y para ocultar los no-objetivo).
        var allWorksets = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .ToList();
        var nameById = allWorksets.ToDictionary(w => w.Id, w => w.Name);
        var allIds   = allWorksets.Select(w => w.Id).ToList();

        // Tipo de vista 3D.
        var vft = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);

        if (vft is null)
        {
            _logger.Warning("WorksetService.CreateViewsPerWorkset: no hay un ViewFamilyType 3D.");
            result.Errors.Add((string.Empty, "No se encontró un tipo de vista 3D en el documento."));
            return result;
        }

        // Nombres de vista existentes (para garantizar unicidad).
        var existingViewNames = new HashSet<string>(
            new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Name),
            StringComparer.OrdinalIgnoreCase);

        var prefix = viewNamePrefix ?? string.Empty;

        _tx.Run(doc, "FamilyReviser: create 3D views per workset", txDoc =>
        {
            foreach (var targetId in targetIds)
            {
                if (!nameById.TryGetValue(targetId, out var wsName))
                {
                    result.Errors.Add(($"(id {targetId.IntegerValue})", "El workset ya no existe."));
                    continue;
                }

                try
                {
                    var view = View3D.CreateIsometric(txDoc, vft.Id);

                    var baseName   = $"{prefix}{wsName}";
                    var uniqueName = WorksetViewCreateResult.MakeUniqueViewName(baseName, existingViewNames);
                    try { view.Name = uniqueName; }
                    catch { /* si el nombre falla, se queda el autogenerado */ }

                    // Aislar el workset objetivo: visible él, ocultos los demás.
                    foreach (var wsId in allIds)
                    {
                        var vis = wsId == targetId ? WorksetVisibility.Visible : WorksetVisibility.Hidden;
                        try { view.SetWorksetVisibility(wsId, vis); }
                        catch { /* algún workset puede no admitirse en esta vista */ }
                    }

                    result.Created.Add((wsName, view.Name));
                }
                catch (Exception ex)
                {
                    result.Errors.Add((wsName, ex.Message));
                }
            }
        });

        _logger.Information(
            "WorksetService.CreateViewsPerWorkset: {Created} vistas creadas, {Errors} errores.",
            result.Created.Count, result.Errors.Count);

        return result;
    }

    public WorksetViewCreateResult CreateCoordinationView(
        Document doc, IEnumerable<WorksetId> worksetIds, string viewName)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new WorksetViewCreateResult();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.CreateCoordinationView: el documento no es workshared.");
            return result;
        }

        var targetIds = (worksetIds ?? Enumerable.Empty<WorksetId>()).Distinct().ToList();
        if (targetIds.Count == 0)
        {
            _logger.Information("WorksetService.CreateCoordinationView: no se indicaron worksets.");
            return result;
        }

        var targetSet = new HashSet<WorksetId>(targetIds);

        var allIds = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>()
            .Select(w => w.Id)
            .ToList();

        var vft = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);

        if (vft is null)
        {
            _logger.Warning("WorksetService.CreateCoordinationView: no hay un ViewFamilyType 3D.");
            result.Errors.Add((string.Empty, "No se encontró un tipo de vista 3D en el documento."));
            return result;
        }

        var existingViewNames = new HashSet<string>(
            new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Name),
            StringComparer.OrdinalIgnoreCase);

        _tx.Run(doc, "FamilyReviser: create coordination view", txDoc =>
        {
            try
            {
                var view = View3D.CreateIsometric(txDoc, vft.Id);

                var uniqueName = WorksetViewCreateResult.MakeUniqueViewName(
                    string.IsNullOrWhiteSpace(viewName) ? "Coordinación" : viewName.Trim(),
                    existingViewNames);
                try { view.Name = uniqueName; }
                catch { /* si el nombre falla, se queda el autogenerado */ }

                // Visibles los worksets marcados; ocultos los demás.
                foreach (var wsId in allIds)
                {
                    var vis = targetSet.Contains(wsId) ? WorksetVisibility.Visible : WorksetVisibility.Hidden;
                    try { view.SetWorksetVisibility(wsId, vis); }
                    catch { /* algún workset puede no admitirse en esta vista */ }
                }

                result.Created.Add(("(coordinación)", view.Name));
            }
            catch (Exception ex)
            {
                result.Errors.Add(("(coordinación)", ex.Message));
            }
        });

        _logger.Information(
            "WorksetService.CreateCoordinationView: {Created} vista creada, {Errors} errores.",
            result.Created.Count, result.Errors.Count);

        return result;
    }

    private static IReadOnlyList<View> ResolveViews(Document doc, WorksetViewScope scope)
    {
        if (scope == WorksetViewScope.ActiveView)
            return doc.ActiveView is View active ? new List<View> { active } : new List<View>();

        var all = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>();

        return scope switch
        {
            WorksetViewScope.AllViews         => all.Where(v => !v.IsTemplate).ToList(),
            WorksetViewScope.AllViewTemplates => all.Where(v => v.IsTemplate).ToList(),
            _                                 => all.ToList(),
        };
    }

    // ================================================================== //
    //  Auditoría                                                           //
    // ================================================================== //

    public WorksetAuditResult Audit(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new WorksetAuditResult();

        if (!doc.IsWorkshared)
        {
            _logger.Warning("WorksetService.Audit: el documento no es workshared.");
            return result;
        }

        // Worksets vacíos
        var countByWorkset = BuildElementCountByWorkset(doc);

        var worksets = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Cast<Workset>();

        foreach (var w in worksets)
        {
            countByWorkset.TryGetValue(w.Id, out var count);
            if (count == 0)
                result.EmptyWorksets.Add(w.Name);
        }

        _logger.Information(
            "WorksetService.Audit: {Inconsistencies} inconsistencias, {Empty} vacíos.",
            result.Inconsistencies.Count, result.EmptyWorksets.Count);

        return result;
    }

    // ================================================================== //
    //  Helpers privados                                                    //
    // ================================================================== //

    private static IReadOnlyList<ElementId> ResolveScope(Document doc, WorksetAssignRequest request)
    {
        var hasCategory = request.CategoryFilter.Count > 0;
        var hasImports  = request.IncludeImportInstances;
        var hasExplicit = request.ElementIds.Count > 0;

        // Sin ningún scope → todo el modelo.
        if (!hasCategory && !hasImports && !hasExplicit)
        {
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .Distinct()
                .ToList();
        }

        var ids = new HashSet<ElementId>();

        if (hasCategory)
        {
            foreach (var bic in request.CategoryFilter)
                foreach (var id in new FilteredElementCollector(doc)
                             .OfCategory(bic)
                             .WhereElementIsNotElementType()
                             .ToElementIds())
                    ids.Add(id);
        }

        if (hasImports)
        {
            foreach (var id in new FilteredElementCollector(doc)
                         .OfClass(typeof(ImportInstance))
                         .WhereElementIsNotElementType()
                         .ToElementIds())
                ids.Add(id);
        }

        // ElementIds explícitos:
        //   - solos → son el scope
        //   - junto a category/imports → se intersecan con lo recolectado
        if (hasExplicit && !hasCategory && !hasImports)
        {
            ids = new HashSet<ElementId>(request.ElementIds);
        }
        else if (hasExplicit)
        {
            ids.IntersectWith(request.ElementIds);
        }

        return ids.ToList();
    }

    private static Dictionary<WorksetId, int> BuildElementCountByWorkset(Document doc)
    {
        var counts = new Dictionary<WorksetId, int>();

        var param = BuiltInParameter.ELEM_PARTITION_PARAM;

        foreach (var elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            var p = elem.get_Parameter(param);
            if (p is null) continue;
            var wid = new WorksetId(p.AsInteger());
            counts.TryGetValue(wid, out var c);
            counts[wid] = c + 1;
        }

        return counts;
    }
}
