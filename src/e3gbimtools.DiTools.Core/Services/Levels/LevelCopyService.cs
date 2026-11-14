using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Levels;

namespace E3G.BimTools.DiTools.Core.Services.Levels;

/// <summary>
/// Implementacion de <see cref="ILevelCopyService"/>: copia niveles desde un
/// RevitLinkInstance cargado al documento anfitrion, aplicando prefijo/sufijo
/// al nombre y, opcionalmente, creando una vista de planta por cada nivel.
///
/// Decisiones de diseño:
///  - Operacion envuelta en TransactionGroup -> un solo Ctrl-Z para deshacer.
///  - La elevacion se trae aplicando la transformacion del link (BasisZ y
///    Origin.Z), asi un vinculo desplazado en Z aterriza en la cota correcta
///    del anfitrion en lugar de duplicar la cota del link "tal cual".
///  - No se intenta clonar el tipo de nivel (Building/Plenum) — los niveles
///    creados usan el tipo por defecto del anfitrion.  Si Angel necesita esto
///    despues, se anade en un Sprint posterior.
///  - Si una colision de nombre impide crear el nivel, se cuenta como
///    "omitido" en lugar de petar.
/// </summary>
public sealed class LevelCopyService : ILevelCopyService
{
    private readonly ITransactionService _transactions;
    private readonly ILogger _logger;

    public LevelCopyService(ITransactionService transactions, ILoggerFactory loggerFactory)
    {
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _logger = loggerFactory.CreateLogger<LevelCopyService>();
    }

    public IReadOnlyList<RevitLinkOption> GetLoadedLinks(Document hostDocument)
    {
        if (hostDocument is null) throw new ArgumentNullException(nameof(hostDocument));

        var result = new List<RevitLinkOption>();
        var instances = new FilteredElementCollector(hostDocument)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .Where(rli => rli.GetLinkDocument() is not null)
            .ToList();

        // Si hay varias instancias del mismo tipo, anadimos sufijo numerico
        // para que el usuario las distinga en el combo.
        var groupedByName = instances
            .GroupBy(rli => GetLinkName(rli), StringComparer.OrdinalIgnoreCase);

        foreach (var grp in groupedByName)
        {
            var list = grp.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var rli = list[i];
                var display = list.Count == 1 ? grp.Key : $"{grp.Key}  #{i + 1}";
                result.Add(new RevitLinkOption
                {
                    InstanceId = rli.Id.Value,
                    DisplayName = display,
                });
            }
        }

        return result
            .OrderBy(o => o.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<LevelDescriptor> GetLevelsFromLink(Document hostDocument, long linkInstanceId)
    {
        if (hostDocument is null) throw new ArgumentNullException(nameof(hostDocument));

        var rli = hostDocument.GetElement(new ElementId(linkInstanceId)) as RevitLinkInstance;
        if (rli is null) return Array.Empty<LevelDescriptor>();
        var linkDoc = rli.GetLinkDocument();
        if (linkDoc is null) return Array.Empty<LevelDescriptor>();

        // Transformacion del link: solo nos importa el desplazamiento en Z
        // para llevar las elevaciones a coordenadas del anfitrion.
        var linkTransform = rli.GetTotalTransform();
        var deltaZInternal = linkTransform.Origin.Z;

        // Nombres existentes en el anfitrion para marcar duplicados.
        var hostLevelNames = new HashSet<string>(
            new FilteredElementCollector(hostDocument)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l => l.Name),
            StringComparer.OrdinalIgnoreCase);

        var descriptors = new List<LevelDescriptor>();
        var linkLevels = new FilteredElementCollector(linkDoc)
            .OfClass(typeof(Level))
            .Cast<Level>();

        foreach (var lvl in linkLevels)
        {
            var elevInternal = lvl.Elevation + deltaZInternal;
            descriptors.Add(new LevelDescriptor
            {
                LinkedLevelId = lvl.Id.Value,
                Name = lvl.Name,
                ElevationInternal = elevInternal,
                ElevationMeters = ToMeters(elevInternal),
                ExistsInHost = hostLevelNames.Contains(lvl.Name),
            });
        }

        return descriptors
            .OrderBy(d => d.ElevationInternal)
            .ToList();
    }

    public LevelCopyResult CopyLevels(
        Document hostDocument,
        long linkInstanceId,
        IReadOnlyList<LevelDescriptor> levelsToCopy,
        LevelCopyOptions options)
    {
        if (hostDocument is null) throw new ArgumentNullException(nameof(hostDocument));
        if (levelsToCopy is null) throw new ArgumentNullException(nameof(levelsToCopy));
        options ??= new LevelCopyOptions();

        var result = new LevelCopyResult();
        if (levelsToCopy.Count == 0) return result;

        // Pre-resolver ViewFamilyType "FloorPlan" si toca crear vistas — fuera de la
        // transaccion para fallar pronto si no hay tipo disponible.
        ElementId floorPlanTypeId = ElementId.InvalidElementId;
        if (options.CreateFloorPlans)
        {
            floorPlanTypeId = ResolveFloorPlanViewFamilyTypeId(hostDocument);
            if (floorPlanTypeId == ElementId.InvalidElementId)
            {
                result.Warnings.Add(
                    "No se encontro ningun tipo de vista de planta en el anfitrion; " +
                    "los niveles se copiaran pero no se generaran vistas.");
            }
        }

        _transactions.RunGroup(hostDocument, "DiTools: Copiar niveles", () =>
        {
            // Refresca el set de nombres dentro de la transaccion (puede que en
            // ediciones rapidas hayan cambiado entre listar y copiar).
            var hostNames = new HashSet<string>(
                new FilteredElementCollector(hostDocument)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Select(l => l.Name),
                StringComparer.OrdinalIgnoreCase);

            _transactions.Run(hostDocument, "Crear niveles", doc =>
            {
                foreach (var src in levelsToCopy)
                {
                    var finalName = (options.NamePrefix ?? string.Empty) + src.Name + (options.NameSuffix ?? string.Empty);

                    if (hostNames.Contains(finalName))
                    {
                        result.SkippedExisting++;
                        result.Warnings.Add($"Nivel '{finalName}' omitido — ya existe en el documento anfitrion.");
                        continue;
                    }

                    Level? newLevel;
                    try
                    {
                        newLevel = Level.Create(doc, src.ElevationInternal);
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Error creando nivel '{finalName}': {ex.Message}");
                        continue;
                    }

                    if (newLevel is null)
                    {
                        result.Warnings.Add($"Revit devolvio null al crear el nivel '{finalName}'.");
                        continue;
                    }

                    try { newLevel.Name = finalName; }
                    catch (Exception ex)
                    {
                        // El nivel se creo pero no pudimos renombrarlo (colision rara,
                        // caracter ilegal, etc.).  Lo dejamos con el nombre por defecto
                        // y avisamos.
                        result.Warnings.Add(
                            $"Nivel creado pero no se pudo renombrar a '{finalName}': {ex.Message}");
                    }

                    hostNames.Add(newLevel.Name);
                    result.Created++;

                    if (options.CreateFloorPlans && floorPlanTypeId != ElementId.InvalidElementId)
                    {
                        try
                        {
                            var view = ViewPlan.Create(doc, floorPlanTypeId, newLevel.Id);
                            if (view is not null) result.FloorPlansCreated++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add(
                                $"Nivel '{newLevel.Name}' creado, pero falló la vista de planta: {ex.Message}");
                        }
                    }
                }
            });
        });

        _logger.Information("CopyLevels finalizado: {0}", result.ToSummary());
        return result;
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static string GetLinkName(RevitLinkInstance rli)
    {
        try
        {
            var typeId = rli.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var type = rli.Document.GetElement(typeId);
                if (type is not null && !string.IsNullOrWhiteSpace(type.Name))
                    return type.Name;
            }
        }
        catch { /* fallback abajo */ }
        return rli.Name ?? "(vinculo sin nombre)";
    }

    private static ElementId ResolveFloorPlanViewFamilyTypeId(Document doc)
    {
        var vft = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
        return vft?.Id ?? ElementId.InvalidElementId;
    }

    private static double ToMeters(double internalFeet)
    {
        try { return UnitUtils.ConvertFromInternalUnits(internalFeet, UnitTypeId.Meters); }
        catch { return internalFeet * 0.3048; }
    }
}
