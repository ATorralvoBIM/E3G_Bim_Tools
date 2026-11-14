using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DialuxImport;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Services.DialuxImport;

/// <summary>
/// Implementacion de <see cref="IDialuxImportService"/>.
///
/// Cada luminaria colocada lleva una marca de Extensible Storage (archivo de
/// origen, handle del INSERT y nombre de bloque).  Con ella SIMULAR/AUDITAR
/// casan DWG ↔ modelo, REHACER borra solo lo importado de ese DWG y una nueva
/// exportacion de DIALux (handles distintos) se casa por proximidad.
/// </summary>
public sealed class DialuxImportService : IDialuxImportService
{
    private static readonly Guid SchemaGuid = new("5C0B7E21-3D4A-4F8B-9A61-2E7D0C9B8F14");
    private const string FieldSource = "SourceKey";
    private const string FieldHandle = "Handle";
    private const string FieldBlock = "BlockName";

    /// <summary>Distancia maxima para casar por proximidad una luminaria cuyo handle ha cambiado (mm).</summary>
    private const double RematchMm = 1000;
    private const double RotationToleranceRad = Math.PI / 180;

    private static readonly BuiltInCategory[] LightingCategories =
    {
        BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_LightingDevices,
        BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_GenericModel,
        BuiltInCategory.OST_SpecialityEquipment,
    };

    private readonly ILogger _logger;

    public DialuxImportService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<DialuxImportService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // ═════════════════════════════ Lectura ═════════════════════════════

    public DialuxDrawing Read(string filePath)
    {
        try
        {
            var d = new DialuxDwgReader().Read(filePath);
            _logger.Information("DialuxImport.Read: '{File}' → {Inserts} inserciones de {Blocks} bloques en {Layers} capas.",
                filePath, d.Inserts.Count, d.Blocks.Count, d.Layers.Count);
            return d;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DialuxImport.Read: error leyendo '{File}'.", filePath);
            throw;
        }
    }

    public IReadOnlyList<DialuxFamilyType> GetFamilyTypes(Document doc, bool allCategories)
    {
        var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
        if (!allCategories)
            collector = collector.WherePasses(new ElementMulticategoryFilter(LightingCategories));

        return collector.Cast<FamilySymbol>()
            .Where(s => s.Category is { CategoryType: CategoryType.Model } && IsPlaceable(s.Family))
            .Select(s => new DialuxFamilyType
            {
                Id = s.Id,
                FamilyName = s.FamilyName,
                TypeName = s.Name,
                Category = s.Category.Name,
                IsWorkPlaneBased = s.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased,
            })
            .OrderBy(t => t.Category != LightingCategoryName(doc))
            .ThenBy(t => t.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.TypeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string LightingCategoryName(Document doc) =>
        Category.GetCategory(doc, BuiltInCategory.OST_LightingFixtures)?.Name ?? string.Empty;

    /// <summary>
    /// Basadas en nivel o en plano de trabajo (incluye las basadas en cara).  Las
    /// alojadas en techo/muro (OneLevelBasedHosted) necesitan anfitrion y no se ofrecen.
    /// </summary>
    private static bool IsPlaceable(Family f) => f.FamilyPlacementType is
        FamilyPlacementType.OneLevelBased or FamilyPlacementType.WorkPlaneBased;

    public IReadOnlyList<DialuxLevel> GetLevels(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => new DialuxLevel
            {
                Id = l.Id,
                Name = l.Name,
                ElevationM = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters),
            })
            .ToList();

    // ═════════════════════════════ Sugerencias ═════════════════════════════

    public int SuggestMappings(DialuxDrawing drawing, DialuxConfig config, IReadOnlyList<DialuxFamilyType> types)
    {
        int assigned = 0;
        foreach (var block in drawing.Blocks)
        {
            var mapping = config.FindMapping(block.Name);
            if (mapping is { IsMapped: true }) continue;

            var sample = drawing.Inserts.First(i => string.Equals(i.BlockName, block.Name, StringComparison.OrdinalIgnoreCase));
            var best = types
                .Select(t => (t, score: Score(block.Name, sample.Attributes.Values, t)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .FirstOrDefault();
            if (best.t is null) continue;

            if (mapping is null)
            {
                mapping = new DialuxMapping { BlockName = block.Name };
                config.Mappings.Add(mapping);
            }
            mapping.FamilyName = best.t.FamilyName;
            mapping.TypeName = best.t.TypeName;
            assigned++;
        }
        return assigned;
    }

    private static int Score(string blockName, IEnumerable<string> attributeValues, DialuxFamilyType t)
    {
        string bn = Norm(blockName), tn = Norm(t.TypeName), fn = Norm(t.FamilyName);
        if (bn.Length == 0) return 0;
        if (bn == tn || bn == fn || bn == fn + tn) return 100;
        if (tn.Length >= 4 && bn.Contains(tn)) return 80;
        if (bn.Length >= 4 && (tn.Contains(bn) || fn.Contains(bn))) return 70;
        foreach (var v in attributeValues.Select(Norm).Where(v => v.Length >= 4))
            if (v == tn || tn.Contains(v) || fn.Contains(v)) return 60;
        // Palabras sueltas del nombre (p. ej. la referencia "LYA3150C" de la descripcion de DIALux).
        foreach (var w in blockName.Split(new[] { ' ', '·', '-', '_', '/', ',' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(Norm).Where(w => w.Length >= 5 && w.Any(char.IsDigit)))
            if (tn.Contains(w) || fn.Contains(w)) return 50;
        return 0;
    }

    private static string Norm(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // ═════════════════════════════ Alineado ═════════════════════════════

    private const double AlignToleranceM = 0.10;

    public DialuxAlignment AutoAlign(Document doc, DialuxDrawing drawing, DialuxConfig config)
    {
        var level = FindLevel(doc, config.LevelName);
        if (level is null)
            return new DialuxAlignment { Message = "Elige primero el nivel: se usan los pilares de esa planta." };

        // DWG: inserciones de pilares (capa o bloque con "COL"/"PILAR"/"COLUMN").
        double toM = (config.Units ?? drawing.SuggestedUnits).ToMm() / 1000;
        var dwg = drawing.Inserts
            .Where(i => !i.IsDialuxLuminaire && IsColumnName(i.Layer + " " + i.BlockName))
            .Select(i => (X: i.X * toM, Y: i.Y * toM))
            .ToList();
        if (dwg.Count < 3)
            return new DialuxAlignment
            {
                DwgPoints = dwg.Count,
                Message = "El DWG no trae pilares reconocibles (capa o bloque con COL / PILAR). Ajusta el desfase a mano.",
            };

        var revit = CollectColumns(doc, level);
        if (revit.Count < 3)
            return new DialuxAlignment
            {
                DwgPoints = dwg.Count,
                RevitPoints = revit.Count,
                Message = $"No hay pilares en Revit (ni en los vínculos) a la altura del nivel '{level.Name}'.",
            };

        var r = ColumnMatcher.Match(dwg, revit, AlignToleranceM);
        int needed = Math.Max(3, (int)Math.Ceiling(dwg.Count * 0.3));
        if (r is null || r.Value.Matched < needed)
            return new DialuxAlignment
            {
                DwgPoints = dwg.Count,
                RevitPoints = revit.Count,
                Matched = r?.Matched ?? 0,
                Message = $"No se ha encontrado un encaje fiable: coinciden {r?.Matched ?? 0} de {dwg.Count} pilares del DWG " +
                          $"(hacen falta {needed}). Revisa las unidades y el nivel, o ajusta el desfase a mano.",
            };

        var m = r.Value;
        double deg = m.RotationRad * 180 / Math.PI;
        _logger.Information("DialuxImport.AutoAlign: {Matched}/{Dwg} pilares (Revit {Revit}), t=({Tx:0.###},{Ty:0.###}) m, giro {Deg:0.###}°, error medio {Err:0.0} mm.",
            m.Matched, dwg.Count, revit.Count, m.Tx, m.Ty, deg, m.MeanError * 1000);
        return new DialuxAlignment
        {
            Success = true,
            OffsetXMm = Math.Round(m.Tx * 1000, 1),
            OffsetYMm = Math.Round(m.Ty * 1000, 1),
            RotationDeg = Math.Abs(deg) < 1e-6 ? 0 : Math.Round(deg, 4),
            Matched = m.Matched,
            DwgPoints = dwg.Count,
            RevitPoints = revit.Count,
            MeanErrorMm = m.MeanError * 1000,
            Message = $"Encajados {m.Matched} de {dwg.Count} pilares del DWG con los de Revit (error medio {m.MeanError * 1000:0} mm).",
        };
    }

    private static bool IsColumnName(string s)
    {
        var u = s.ToUpperInvariant();
        return u.Contains("COL") || u.Contains("PILAR") || u.Contains("COLUMN");
    }

    /// <summary>
    /// Centros (m, coordenadas internas del modelo) de los pilares del modelo y de
    /// sus vinculos de Revit que atraviesan la planta del nivel (entre +0,5 y +1,5 m).
    /// </summary>
    private static List<(double X, double Y)> CollectColumns(Document doc, Level level)
    {
        double z0 = level.ProjectElevation + 0.5 / 0.3048, z1 = level.ProjectElevation + 1.5 / 0.3048;
        var cats = new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns });
        var pts = new List<(double X, double Y)>();

        void Add(Document d, Transform t)
        {
            foreach (var fi in new FilteredElementCollector(d).WherePasses(cats).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                if (fi.Location is not LocationPoint lp || fi.get_BoundingBox(null) is not { } bb) continue;
                var min = t.OfPoint(bb.Min);
                var max = t.OfPoint(bb.Max);
                if (Math.Min(min.Z, max.Z) > z1 || Math.Max(min.Z, max.Z) < z0) continue;
                var p = t.OfPoint(lp.Point);
                pts.Add((p.X * 0.3048, p.Y * 0.3048));
            }
        }

        Add(doc, Transform.Identity);
        foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            if (link.GetLinkDocument() is { } ld)
                Add(ld, link.GetTotalTransform());
        return pts;
    }

    // ═════════════════════════════ Plan ═════════════════════════════

    public DialuxPlan BuildPlan(Document doc, DialuxDrawing drawing, DialuxConfig config)
    {
        var plan = new DialuxPlan();
        var units = config.Units ?? drawing.SuggestedUnits;
        var map = new CoordinateMap(doc, config, units);
        double tolFt = Math.Max(1, config.ToleranceMm) / 304.8;

        var level = FindLevel(doc, config.LevelName);
        if (level is null)
            plan.Warnings.Add(string.IsNullOrWhiteSpace(config.LevelName)
                ? "Elige el nivel en el que se colocan las luminarias."
                : $"No existe el nivel '{config.LevelName}'.");

        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .GroupBy(s => Key(s.FamilyName, s.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var layers = config.Layers.Count > 0 ? new HashSet<string>(config.Layers, StringComparer.OrdinalIgnoreCase) : null;
        var existing = CollectExisting(doc, drawing.SourceKey);
        var byHandle = existing.GroupBy(e => e.Handle).ToDictionary(g => g.Key, g => g.First());
        var used = new HashSet<ElementId>();
        int unmapped = 0, mirrored = 0;

        // 1) Filas del DWG con su destino.  Primero las que casan por handle, luego el resto por proximidad.
        var pending = new List<(DialuxPlanRow Row, FamilySymbol? Symbol)>();
        foreach (var ins in drawing.Inserts)
        {
            if (layers is not null && !layers.Contains(ins.Layer)) continue;
            var m = config.FindMapping(ins.BlockName);
            if (m is null || !m.IsMapped) { unmapped++; continue; }
            if (ins.Mirrored) mirrored++;

            var (pt, rot) = map.Map(ins, m.ExtraRotationDeg * Math.PI / 180);
            double z = m.HeightSource == DialuxHeightSource.DwgZ ? ins.Z * units.ToFeet() : m.HeightMm / 304.8;
            symbols.TryGetValue(Key(m.FamilyName, m.TypeName), out var symbol);

            var row = new DialuxPlanRow
            {
                Source = ins,
                Mapping = m,
                Target = new XYZ(pt.X, pt.Y, z),
                TargetRotation = rot,
            };
            plan.Rows.Add(row);
            pending.Add((row, symbol));

            if (byHandle.TryGetValue(ins.Handle, out var ex) && used.Add(ex.Instance.Id))
                Compare(row, ex, symbol, tolFt);
        }

        foreach (var (row, symbol) in pending.Where(p => p.Row.ExistingId is null))
        {
            var near = existing
                .Where(e => !used.Contains(e.Instance.Id)
                            && string.Equals(e.Block, row.BlockName, StringComparison.OrdinalIgnoreCase))
                .Select(e => (e, d: e.Point is null ? double.MaxValue : Dist2D(e.Point, row.Target!)))
                .Where(x => x.d * 304.8 <= RematchMm)
                .OrderBy(x => x.d)
                .FirstOrDefault();
            if (near.e is not null && used.Add(near.e.Instance.Id))
                Compare(row, near.e, symbol, tolFt);
            else
                row.Status = DialuxRowStatus.New;
        }

        // 2) Errores que impiden colocar.
        foreach (var (row, symbol) in pending)
        {
            if (symbol is null)
            {
                row.Status = DialuxRowStatus.Error;
                row.Detail = $"No existe el tipo '{row.Mapping!.TargetLabel}' en el modelo.";
            }
            else if (level is null && row.Status == DialuxRowStatus.New)
            {
                row.Status = DialuxRowStatus.Error;
                row.Detail = "Sin nivel.";
            }
        }

        // 3) Importadas de este DWG que ya no estan.
        foreach (var e in existing.Where(e => !used.Contains(e.Instance.Id)))
            plan.Rows.Add(new DialuxPlanRow
            {
                Status = DialuxRowStatus.Extra,
                Mapping = new DialuxMapping { BlockName = e.Block },
                ExistingId = e.Instance.Id,
                ExistingType = Key(e.Instance.Symbol.FamilyName, e.Instance.Symbol.Name),
                Detail = $"Handle {e.Handle}: ya no está en el DWG (o su bloque ya no se mapea / su capa está filtrada).",
            });

        if (unmapped > 0)
            plan.Warnings.Add($"{unmapped} inserción(es) de bloques sin tipo asignado se ignoran.");
        if (mirrored > 0)
            plan.Warnings.Add($"{mirrored} luminaria(s) están en simetría en el DWG; se colocan sin simetría.");

        _logger.Information("DialuxImport.BuildPlan: '{Key}' → {Summary}.", drawing.SourceKey, plan.ToSummary());
        return plan;
    }

    private static void Compare(DialuxPlanRow row, Existing ex, FamilySymbol? symbol, double tolFt)
    {
        row.ExistingId = ex.Instance.Id;
        row.ExistingType = Key(ex.Instance.Symbol.FamilyName, ex.Instance.Symbol.Name);
        row.DeltaMm = ex.Point is null ? 0 : Dist2D(ex.Point, row.Target!) * 304.8;
        double dRot = Math.Abs(NormalizeAngle(ex.Rotation - row.TargetRotation));

        var notes = new List<string>();
        bool moved = row.DeltaMm > tolFt * 304.8 || dRot > RotationToleranceRad;
        if (row.DeltaMm > tolFt * 304.8) notes.Add($"desplazada {row.DeltaMm:0} mm");
        if (dRot > RotationToleranceRad) notes.Add($"girada {dRot * 180 / Math.PI:0.#}°");
        bool wrongType = symbol is not null && ex.Instance.Symbol.Id != symbol.Id;
        if (wrongType) notes.Add($"tipo actual '{row.ExistingType}'");

        row.Status = wrongType ? DialuxRowStatus.WrongType : moved ? DialuxRowStatus.Moved : DialuxRowStatus.Ok;
        row.Detail = string.Join(", ", notes);
    }

    // ═════════════════════════════ Aplicar ═════════════════════════════

    public DialuxResult Apply(Document doc, DialuxDrawing drawing, DialuxConfig config, DialuxAction action)
    {
        var plan = BuildPlan(doc, drawing, config);
        var level = FindLevel(doc, config.LevelName);
        if (level is null && action != DialuxAction.DeleteExtra)
            return new DialuxResult { Success = false, ErrorMessage = plan.Warnings.FirstOrDefault() ?? "Sin nivel." };

        var result = new DialuxResult();
        result.Warnings.AddRange(plan.Warnings);
        string label = action switch
        {
            DialuxAction.Create => "Crear",
            DialuxAction.Redo => "Rehacer",
            DialuxAction.DeleteExtra => "Borrar sobrantes",
            _ => "Sincronizar",
        };

        using var tx = new Transaction(doc, $"DIALux → Revit: {label}");
        var opts = tx.GetFailureHandlingOptions();
        opts.SetFailuresPreprocessor(new SwallowWarnings());
        tx.SetFailureHandlingOptions(opts);
        tx.Start();
        try
        {
            var schema = GetOrCreateSchema();

            if (action is DialuxAction.Redo or DialuxAction.DeleteExtra)
            {
                var toDelete = plan.Rows
                    .Where(r => r.ExistingId is not null && (action == DialuxAction.Redo || r.Status == DialuxRowStatus.Extra))
                    .Select(r => r.ExistingId!).ToList();
                if (toDelete.Count > 0) result.Deleted = doc.Delete(toDelete).Count(id => toDelete.Contains(id));
            }

            foreach (var row in action == DialuxAction.DeleteExtra ? Enumerable.Empty<DialuxPlanRow>() : plan.Rows)
            {
                try
                {
                    switch (row.Status)
                    {
                        case DialuxRowStatus.New:
                        case DialuxRowStatus.Ok or DialuxRowStatus.Moved or DialuxRowStatus.WrongType when action == DialuxAction.Redo:
                            Place(doc, row, level!, drawing.SourceKey, schema);
                            result.Created++;
                            break;
                        case DialuxRowStatus.Moved or DialuxRowStatus.WrongType when action == DialuxAction.Sync:
                            Update(doc, row);
                            result.Updated++;
                            break;
                        case DialuxRowStatus.Error:
                            result.Warnings.Add($"{row.BlockName} ({row.Source?.Handle}): {row.Detail}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{row.BlockName} ({row.Source?.Handle}): {ex.Message}");
                    _logger.Warning("DialuxImport.Apply: fallo en {Block} {Handle}: {Msg}", row.BlockName, row.Source?.Handle ?? "", ex.Message);
                }
            }

            int extra = plan.Count(DialuxRowStatus.Extra);
            if (extra > 0 && action is DialuxAction.Create or DialuxAction.Sync)
                result.Warnings.Add($"{extra} luminaria(s) importadas de este DWG ya no están en él; no se han borrado (usa Rehacer o bórralas desde la auditoría).");

            tx.Commit();
            _logger.Information("DialuxImport.Apply({Action}): {Summary}", action, result.ToSummary());
            return result;
        }
        catch (Exception ex)
        {
            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
            _logger.Error(ex, "DialuxImport.Apply: error.");
            return new DialuxResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static void Place(Document doc, DialuxPlanRow row, Level level, string sourceKey, Schema schema)
    {
        var m = row.Mapping!;
        var symbol = FindSymbol(doc, m.FamilyName, m.TypeName)
            ?? throw new InvalidOperationException($"No existe el tipo '{m.TargetLabel}'.");
        if (!symbol.IsActive)
        {
            symbol.Activate();
            doc.Regenerate();
        }

        var t = row.Target!;
        var pt = new XYZ(t.X, t.Y, level.ProjectElevation);
        FamilyInstance fi;
        if (symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased)
        {
            fi = doc.Create.NewFamilyInstance(pt, symbol, level, StructuralType.NonStructural);
            if (Math.Abs(row.TargetRotation) > 1e-9)
                ElementTransformUtils.RotateElement(doc, fi.Id, Line.CreateBound(pt, pt + XYZ.BasisZ), row.TargetRotation);
        }
        else
        {
            // Basadas en plano de trabajo / cara: se alojan en el plano del nivel y se orientan con la direccion de referencia.
            var dir = new XYZ(Math.Cos(row.TargetRotation), Math.Sin(row.TargetRotation), 0);
            fi = doc.Create.NewFamilyInstance(level.GetPlaneReference(), pt, dir, symbol);
        }

        SetHeight(fi, t.Z);

        var entity = new Entity(schema);
        entity.Set(FieldSource, sourceKey);
        entity.Set(FieldHandle, row.Source!.Handle);
        entity.Set(FieldBlock, row.Source.BlockName);
        fi.SetEntity(entity);
    }

    private static void Update(Document doc, DialuxPlanRow row)
    {
        if (doc.GetElement(row.ExistingId!) is not FamilyInstance fi) return;
        var m = row.Mapping!;

        if (row.Status == DialuxRowStatus.WrongType
            && FindSymbol(doc, m.FamilyName, m.TypeName) is { } symbol && fi.Symbol.Id != symbol.Id)
        {
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
            fi.ChangeTypeId(symbol.Id);
        }

        var t = row.Target!;
        if (fi.Location is LocationPoint lp)
        {
            var cur = lp.Point;
            var delta = new XYZ(t.X - cur.X, t.Y - cur.Y, 0);
            if (!delta.IsZeroLength()) ElementTransformUtils.MoveElement(doc, fi.Id, delta);
            double dRot = NormalizeAngle(row.TargetRotation - HandAngle(fi));
            if (Math.Abs(dRot) > 1e-6)
            {
                var c = new XYZ(t.X, t.Y, cur.Z);
                ElementTransformUtils.RotateElement(doc, fi.Id, Line.CreateBound(c, c + XYZ.BasisZ), dRot);
            }
        }
        SetHeight(fi, t.Z);
    }

    private static void SetHeight(FamilyInstance fi, double heightFt)
    {
        foreach (var bip in new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM })
        {
            var p = fi.get_Parameter(bip);
            if (p is { IsReadOnly: false, StorageType: StorageType.Double })
            {
                p.Set(heightFt);
                return;
            }
        }
    }

    // ═════════════════════════════ Existentes / marca ═════════════════════════════

    private sealed record Existing(FamilyInstance Instance, string Handle, string Block, XYZ? Point, double Rotation);

    private static List<Existing> CollectExisting(Document doc, string sourceKey)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return new List<Existing>();

        var list = new List<Existing>();
        foreach (var fi in new FilteredElementCollector(doc)
                     .OfClass(typeof(FamilyInstance))
                     .WherePasses(new ExtensibleStorageFilter(SchemaGuid))
                     .Cast<FamilyInstance>())
        {
            var e = fi.GetEntity(schema);
            if (!e.IsValid()) continue;
            if (!string.Equals(e.Get<string>(FieldSource), sourceKey, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new Existing(fi, e.Get<string>(FieldHandle), e.Get<string>(FieldBlock),
                (fi.Location as LocationPoint)?.Point, HandAngle(fi)));
        }
        return list;
    }

    private static Schema GetOrCreateSchema()
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is not null) return schema;

        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("E3G_DialuxImport");
        b.SetDocumentation("Luminaria colocada por e3gbimtools DIALux → Revit: archivo de origen, handle y bloque del DWG.");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        // Revit rechaza "E3G" (los vendor id de Extensible Storage piden al menos 4 caracteres);
        // con acceso Public el valor no limita nada, asi que se usa el primero que Revit acepte.
        b.SetVendorId(new[] { "E3GB", "es.e3g", "E3G_BIMTOOLS" }.First(SchemaBuilder.VendorIdIsValid));
        b.AddSimpleField(FieldSource, typeof(string));
        b.AddSimpleField(FieldHandle, typeof(string));
        b.AddSimpleField(FieldBlock, typeof(string));
        return b.Finish();
    }

    // ═════════════════════════════ Utilidades ═════════════════════════════

    private static Level? FindLevel(Document doc, string name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    private static FamilySymbol? FindSymbol(Document doc, string family, string type) =>
        new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(s => string.Equals(s.FamilyName, family, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(s.Name, type, StringComparison.OrdinalIgnoreCase));

    private static string Key(string family, string type) => $"{family} : {type}";

    private static double HandAngle(FamilyInstance fi)
    {
        var h = fi.HandOrientation;
        return Math.Atan2(h.Y, h.X);
    }

    private static double Dist2D(XYZ a, XYZ b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double NormalizeAngle(double a)
    {
        a %= 2 * Math.PI;
        if (a > Math.PI) a -= 2 * Math.PI;
        if (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    /// <summary>DWG (unidades de dibujo) → coordenadas internas de Revit (pies) segun el origen elegido.</summary>
    private sealed class CoordinateMap
    {
        private readonly double _unitFt, _cos, _sin, _rot, _offX, _offY;
        private readonly Transform _toInternal;

        public CoordinateMap(Document doc, DialuxConfig cfg, LegendUnits units)
        {
            _unitFt = units.ToFeet();
            _rot = cfg.RotationDeg * Math.PI / 180;
            _cos = Math.Cos(_rot);
            _sin = Math.Sin(_rot);
            _offX = cfg.OffsetXMm / 304.8;
            _offY = cfg.OffsetYMm / 304.8;
            _toInternal = cfg.Origin switch
            {
                DialuxOrigin.ProjectBasePoint => Transform.CreateTranslation(Flat(BasePoint.GetProjectBasePoint(doc).Position)),
                DialuxOrigin.SharedCoordinates => doc.ActiveProjectLocation.GetTotalTransform().Inverse,
                _ => Transform.Identity,
            };
        }

        public (XYZ Point, double Rotation) Map(DialuxInsert i, double extraRot)
        {
            double x = i.X * _unitFt, y = i.Y * _unitFt;
            var p = _toInternal.OfPoint(new XYZ(_cos * x - _sin * y + _offX, _sin * x + _cos * y + _offY, 0));
            double a = i.Rotation + _rot + extraRot;
            var dir = _toInternal.OfVector(new XYZ(Math.Cos(a), Math.Sin(a), 0));
            return (new XYZ(p.X, p.Y, 0), Math.Atan2(dir.Y, dir.X));
        }

        private static XYZ Flat(XYZ p) => new(p.X, p.Y, 0);
    }

    /// <summary>Descarta los avisos (no los errores) para no interrumpir con cientos de dialogos.</summary>
    private sealed class SwallowWarnings : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var f in accessor.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(f);
            return FailureProcessingResult.Continue;
        }
    }
}
