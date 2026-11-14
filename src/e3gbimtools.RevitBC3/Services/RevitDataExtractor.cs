using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion del extractor.  Algoritmo:
/// 1. Pre-crea una MeasurementResult vacia por cada regla activa, para que
///    TODAS las reglas aparezcan en el resultado aunque no matcheen nada
///    (asi el usuario detecta facilmente reglas que no estan capturando).
/// 2. Agrupa reglas por categoria Revit.
/// 3. Para cada categoria con reglas activas, recorre los elementos UNA vez.
/// 4. Para cada elemento, evalua las reglas en orden de prioridad descendente.
///    La primera que matchee es la que se aplica (gana la mas prioritaria).
/// 5. Calcula la medicion segun el tipo: Length/Area/Volume/Count o parametro
///    custom.  Convierte de unidades internas (feet) a metricas (m, m2, m3).
/// 6. Aplica UnitFactor de la regla.
/// 7. Acumula en MeasurementResult por codigo BC3.  Si dos reglas tienen el
///    mismo BC3Code, contribuyen ambas a la misma partida y se registran en
///    ContributingRules.
/// 8. Resuelve el capitulo: usa el de la regla si lo tiene; si no, lo busca
///    en la jerarquia del BC3 fuente.
/// 9. Los elementos que no matchean ninguna regla se registran como huerfanos.
/// </summary>
public sealed class RevitDataExtractor : IRevitDataExtractor
{
    private readonly IMappingRuleEngine _engine;

    public RevitDataExtractor(IMappingRuleEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ExtractionResult ExtractAll(Document doc, IReadOnlyList<MappingRule> rules, BC3Document? bc3Document = null)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (rules is null) throw new ArgumentNullException(nameof(rules));

        var result = new ExtractionResult
        {
            RulesEvaluated = rules.Count(r => r.Enabled),
        };

        // Solo trabajamos con reglas habilitadas.
        var activeRules = rules.Where(r => r.Enabled).ToList();

        // Agrupa por categoria — y dentro de cada categoria por prioridad descendente.
        var rulesByCategory = activeRules
            .GroupBy(r => r.Category)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Priority).ToList());

        // Acumulador de mediciones — UNA FILA POR REGLA (key = Rule.Id).
        // Esto permite que dos reglas con el mismo BC3Code aparezcan como
        // filas separadas en la previsualizacion (ej. FECALS y GRISES que
        // miden tuberias distintas pero apuntan a la misma partida ED15B873).
        // La agregacion final al exportar se hace en BC3WriterService.
        var measurementByRule = new Dictionary<string, MeasurementResult>(StringComparer.Ordinal);

        // Si tenemos BC3 fuente, indexamos para enriquecer descripciones/unidades.
        var bc3Index = bc3Document?.Concepts ?? new Dictionary<string, BC3Concept>();
        // Y construimos un mapa codigo->capitulo padre desde la jerarquia.
        var chapterByCode = BuildChapterMap(bc3Document);

        // ====================================================================
        // 1. Pre-creacion de MeasurementResult por cada regla activa.
        //    Garantiza que TODAS las reglas aparezcan en el resultado, incluso
        //    si no matchean ningun elemento (asi el usuario ve los "huecos").
        // ====================================================================
        foreach (var rule in activeRules)
        {
            if (string.IsNullOrWhiteSpace(rule.BC3Code)) continue;
            EnsureMeasurement(measurementByRule, rule, bc3Index, chapterByCode);
        }

        // Acumulador de huerfanos por (categoria + tipo).
        var orphanGroups = new Dictionary<string, OrphanGroup>(StringComparer.Ordinal);

        // ====================================================================
        // Recorre TODO el modelo una sola vez (categorias medibles).  De este
        // modo, los elementos en categorias SIN regla activa tambien se
        // registran como huerfanos — antes solo se miraban las categorias
        // con reglas, lo que escondia mediciones pendientes (Angel, 2026-05-20).
        // ====================================================================
        var allCollector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

        foreach (Element element in allCollector)
        {
            var cat = element.Category;
            if (cat is null) continue;
            // Solo categorias de modelo (igual filtro que RevitCategoryProvider:
            // ignora anotacion, vistas, niveles, ejes, camaras, lineas, etc.).
            if (cat.CategoryType != CategoryType.Model) continue;
            var bic = cat.BuiltInCategory;
            if (bic == BuiltInCategory.INVALID) continue;
            if (IsNonMeasurableModelCategory(bic)) continue;

            result.ElementsAnalyzed++;

            // ¿Hay reglas activas para esta categoria?
            if (!rulesByCategory.TryGetValue(bic, out var categoryRules) || categoryRules.Count == 0)
            {
                AccumulateOrphan(element, orphanGroups);
                continue;
            }

            // Busca la primera regla (mas prioritaria) que matchee.
            MappingRule? matched = null;
            foreach (var rule in categoryRules)
            {
                if (_engine.Evaluate(element, rule))
                {
                    matched = rule;
                    break;
                }
            }

            if (matched is null)
            {
                AccumulateOrphan(element, orphanGroups);
                continue;
            }

            result.ElementsMatched++;

            // Calcula la medicion para este elemento.
            var rawValue = ComputeMeasurement(element, matched.Measurement);
            var metricValue = ToMetric(rawValue, matched.Measurement);
            var finalValue = metricValue * matched.UnitFactor;

            AccumulateMeasurement(measurementByRule, matched, element, finalValue, bc3Index, chapterByCode, result);
        }

        result.Measurements = measurementByRule.Values
            .OrderBy(m => m.ChapterDisplay, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.BC3Code, StringComparer.Ordinal)
            .ToList();

        result.Orphans = orphanGroups.Values
            .OrderByDescending(g => g.Count)
            .ToList();

        return result;
    }

    // ============================================================
    // Helpers
    // ============================================================

    /// <summary>
    /// Garantiza que existe una MeasurementResult para la regla concreta
    /// (clave = <see cref="MappingRule.Id"/>, con fallback a BC3Code si no
    /// hay Id).  Si ya existe, devuelve la existente.  Si no, la crea con
    /// metadata enriquecida desde el BC3 fuente.
    ///
    /// NOTA: cada regla genera SU PROPIA fila aunque varias compartan el
    /// mismo BC3Code.  Asi el usuario ve por separado lo que mide cada
    /// regla.  La agregacion para el archivo BC3 final (que exige codigos
    /// unicos) se hace en BC3WriterService.
    /// </summary>
    private static MeasurementResult EnsureMeasurement(
        Dictionary<string, MeasurementResult> bucket,
        MappingRule rule,
        IReadOnlyDictionary<string, BC3Concept> bc3Index,
        IReadOnlyDictionary<string, string> chapterByCode)
    {
        var key = string.IsNullOrEmpty(rule.Id) ? rule.BC3Code : rule.Id;

        if (!bucket.TryGetValue(key, out var mr))
        {
            mr = new MeasurementResult
            {
                BC3Code = rule.BC3Code,
                ChapterDisplay = ResolveChapter(rule, chapterByCode),
            };

            // Resolucion de descripcion/unidad/precio con prioridad:
            // 1. BC3 fuente cargado (catalogo actual).
            // 2. Cache de la regla (Catalog* — guardado en disco con el mapping).
            // 3. Fallback generico (nombre de regla / categoria).
            //
            // Esto asegura que el preview funciona aunque el usuario haya
            // reimportado un BC3 distinto que no contiene el codigo: la regla
            // conserva los datos que vio cuando se asocio al codigo y los
            // sigue mostrando.
            if (bc3Index.TryGetValue(rule.BC3Code, out var concept))
            {
                mr.Description = FirstNonEmpty(
                    concept.Summary,
                    rule.CatalogSummary,
                    string.IsNullOrEmpty(rule.Name) ? rule.CategoryDisplayName + " → " + rule.BC3Code : rule.Name);
                mr.Unit = FirstNonEmpty(
                    concept.Unit,
                    rule.CatalogUnit,
                    DefaultUnitFor(rule.Measurement.BuiltIn));
                mr.PriceUnit = concept.Price > 0 ? concept.Price : rule.CatalogPrice;
            }
            else
            {
                mr.Description = FirstNonEmpty(
                    rule.CatalogSummary,
                    string.IsNullOrEmpty(rule.Name) ? rule.CategoryDisplayName + " → " + rule.BC3Code : rule.Name);
                mr.Unit = FirstNonEmpty(rule.CatalogUnit, DefaultUnitFor(rule.Measurement.BuiltIn));
                mr.PriceUnit = rule.CatalogPrice;
            }
            bucket[key] = mr;
        }

        // Registrar regla contribuyente (siempre será solo una porque la key
        // es la propia regla, pero conservamos la coleccion por compatibilidad
        // con el resto de la UI / diagnostico).
        var ruleLabel = string.IsNullOrEmpty(rule.Name) ? rule.BC3Code : rule.Name;
        if (!string.IsNullOrEmpty(rule.Id) && !mr.ContributingRuleIds.Contains(rule.Id))
        {
            mr.ContributingRules.Add(ruleLabel);
            mr.ContributingRuleIds.Add(rule.Id);
        }
        else if (string.IsNullOrEmpty(rule.Id)
                 && !mr.ContributingRules.Contains(ruleLabel, StringComparer.OrdinalIgnoreCase))
        {
            mr.ContributingRules.Add(ruleLabel);
        }

        return mr;
    }

    private static void AccumulateMeasurement(
        Dictionary<string, MeasurementResult> bucket,
        MappingRule rule,
        Element element,
        double value,
        IReadOnlyDictionary<string, BC3Concept> bc3Index,
        IReadOnlyDictionary<string, string> chapterByCode,
        ExtractionResult result)
    {
        var mr = EnsureMeasurement(bucket, rule, bc3Index, chapterByCode);
        mr.Quantity += value;
        mr.ElementCount++;
        mr.ElementIds.Add(element.Id.Value);

        var (ifcGuid, guidSource) = ReadIfcGuid(element);

        // Acumula contadores de cobertura en el resultado global.
        switch (guidSource)
        {
            case IfcGuidSource.Parameter:  result.GuidFromParameter++; break;
            case IfcGuidSource.Calculated: result.GuidCalculated++;    break;
            default:                       result.GuidFallback++;      break;
        }

        mr.ElementContributions.Add(new ElementMeasurementContribution
        {
            ElementId     = element.Id.Value,
            IfcGuid       = ifcGuid,
            IfcGuidSource = guidSource,
            Quantity      = value,
            SystemName    = ReadSystemName(element),
            LevelName     = ReadLevelName(element),
        });
    }

    /// <summary>
    /// Lee el IfcGUID del elemento e indica su origen.
    /// <list type="number">
    ///   <item>Intenta leer el parametro built-in <c>IFC_GUID</c>.</item>
    ///   <item>Intenta leer el parametro compartido <c>IfcGUID</c>.</item>
    ///   <item>Calcula el GUID deterministicamente desde el <c>UniqueId</c>
    ///         de Revit con <see cref="RevitIfcGuidCalculator"/> (mismo
    ///         algoritmo que usa Revit al exportar a IFC).</item>
    ///   <item>Fallback de emergencia: prefijo numerico basado en ElementId.</item>
    /// </list>
    /// </summary>
    private static (string Guid, IfcGuidSource Source) ReadIfcGuid(Element element)
    {
        // 1. Parametro built-in IFC_GUID (disponible si el modelo se exporto a IFC).
        try
        {
            var p = element.get_Parameter(BuiltInParameter.IFC_GUID);
            if (p is not null && p.HasValue && p.StorageType == StorageType.String)
            {
                var v = p.AsString();
                if (!string.IsNullOrWhiteSpace(v)) return (v.Trim(), IfcGuidSource.Parameter);
            }
        }
        catch { /* sigue */ }

        // 2. Parametro compartido "IfcGUID" (presente en algunas plantillas BIM).
        try
        {
            var pp = element.LookupParameter("IfcGUID");
            if (pp is not null && pp.HasValue && pp.StorageType == StorageType.String)
            {
                var v = pp.AsString();
                if (!string.IsNullOrWhiteSpace(v)) return (v.Trim(), IfcGuidSource.Parameter);
            }
        }
        catch { /* sigue */ }

        // 3. Calcular deterministicamente desde el UniqueId de Revit.
        //    Produce el mismo GUID que Revit asignaria al exportar a IFC.
        try
        {
            var uid = element.UniqueId;
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var calculated = RevitIfcGuidCalculator.FromUniqueId(uid);
                if (calculated is not null) return (calculated, IfcGuidSource.Calculated);
            }
        }
        catch { /* sigue */ }

        // 4. Fallback de emergencia (no deberia ocurrir con elementos validos).
        var fallback = "E" + element.Id.Value.ToString(CultureInfo.InvariantCulture);
        return (fallback, IfcGuidSource.Fallback);
    }

    /// <summary>
    /// Lee el nombre del sistema MEP del elemento.
    /// Para tuberias/conductos/bandejas usa RBS_SYSTEM_NAME_PARAM; si no existe,
    /// sube al MEPSystem padre.  Para el resto devuelve vacio.
    /// </summary>
    private static string ReadSystemName(Element element)
    {
        try
        {
            var p = element.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
            if (p is not null && p.HasValue && p.StorageType == StorageType.String)
            {
                var v = p.AsString();
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
        }
        catch { /* sigue */ }

        try
        {
            if (element is MEPCurve mep)
            {
                var sys = mep.MEPSystem;
                if (sys is not null && !string.IsNullOrWhiteSpace(sys.Name))
                    return sys.Name.Trim();
            }
        }
        catch { /* sigue */ }

        return string.Empty;
    }

    /// <summary>
    /// Lee el nombre del nivel (planta) del elemento.
    /// Prueba primero RBS_START_LEVEL_PARAM (MEP) y luego FAMILY_LEVEL_PARAM (familias).
    /// </summary>
    private static string ReadLevelName(Element element)
    {
        foreach (var bip in new[]
        {
            BuiltInParameter.RBS_START_LEVEL_PARAM,
            BuiltInParameter.FAMILY_LEVEL_PARAM,
        })
        {
            try
            {
                var p = element.get_Parameter(bip);
                if (p is not null && p.HasValue && p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (id != ElementId.InvalidElementId)
                    {
                        var level = element.Document.GetElement(id) as Level;
                        if (level is not null && !string.IsNullOrWhiteSpace(level.Name))
                            return level.Name.Trim();
                    }
                }
            }
            catch { /* sigue */ }
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c)) return c!;
        return string.Empty;
    }

    /// <summary>
    /// Devuelve la cadena de capitulo a mostrar.  Prioridad:
    /// 1. Si la regla la define explicitamente, usa la suya.
    /// 2. Si no, busca el capitulo padre del BC3Code en el BC3 fuente.
    /// 3. Si tampoco, devuelve "(sin capitulo)".
    /// </summary>
    private static string ResolveChapter(MappingRule rule, IReadOnlyDictionary<string, string> chapterByCode)
    {
        if (rule is null) return "(sin capitulo)";

        var explicitChapter = rule.ChapterDisplay;
        if (!string.IsNullOrWhiteSpace(explicitChapter) && explicitChapter != "(sin capitulo)")
            return explicitChapter;

        if (!string.IsNullOrEmpty(rule.BC3Code)
            && chapterByCode.TryGetValue(rule.BC3Code, out var parent)
            && !string.IsNullOrWhiteSpace(parent))
        {
            return parent;
        }

        return "(sin capitulo)";
    }

    /// <summary>
    /// Construye un mapa codigo-de-concepto -> "CODIGO_PADRE — Resumen" recorriendo
    /// la jerarquia del BC3 fuente.  Permite resolver el capitulo de cualquier
    /// partida cuando la regla no lo define.
    /// </summary>
    private static Dictionary<string, string> BuildChapterMap(BC3Document? bc3Document)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bc3Document is null) return map;

        foreach (var root in bc3Document.Roots)
            WalkBuildingMap(root, currentChapter: null, bc3Document, map);

        return map;
    }

    private static void WalkBuildingMap(
        BC3Concept node,
        BC3Concept? currentChapter,
        BC3Document doc,
        Dictionary<string, string> map)
    {
        if (node is null) return;

        var chapterForChildren = node.Type == ConceptType.Capitulo ? node : currentChapter;

        if (currentChapter is not null && !map.ContainsKey(node.Code))
            map[node.Code] = FormatChapter(currentChapter);

        if (node.Type == ConceptType.Capitulo && !map.ContainsKey(node.Code))
            map[node.Code] = FormatChapter(node);

        foreach (var dec in node.Children)
        {
            if (doc.Concepts.TryGetValue(dec.ChildCode, out var child))
                WalkBuildingMap(child, chapterForChildren, doc, map);
        }
    }

    private static string FormatChapter(BC3Concept chapter)
    {
        var resumen = string.IsNullOrWhiteSpace(chapter.Summary) ? string.Empty : chapter.Summary.Trim();
        if (resumen.Length > 60) resumen = resumen.Substring(0, 57) + "...";
        return string.IsNullOrEmpty(resumen) ? chapter.Code : $"{chapter.Code} — {resumen}";
    }

    /// <summary>
    /// Categorias de tipo Model que NO representan algo medible para
    /// presupuestar (camaras, lineas de seccion, niveles, ejes, etc.).
    /// Duplica el filtro de <see cref="RevitCategoryProvider"/> para que el
    /// extractor pueda recorrer el modelo completo sin pasar por el provider.
    /// </summary>
    private static bool IsNonMeasurableModelCategory(BuiltInCategory bic)
    {
        switch (bic)
        {
            case BuiltInCategory.OST_Cameras:
            case BuiltInCategory.OST_SectionBox:
            case BuiltInCategory.OST_Levels:
            case BuiltInCategory.OST_Grids:
            case BuiltInCategory.OST_ReferencePoints:
            case BuiltInCategory.OST_Lines:
            case BuiltInCategory.OST_VolumeOfInterest:
            case BuiltInCategory.OST_Constraints:
            case BuiltInCategory.OST_ProjectInformation:
            case BuiltInCategory.OST_SiteProperty:
            case BuiltInCategory.OST_HVAC_Zones:
            case BuiltInCategory.OST_AreaSchemes:
            case BuiltInCategory.OST_RvtLinks:
            case BuiltInCategory.OST_DesignOptionSets:
            case BuiltInCategory.OST_DesignOptions:
            case BuiltInCategory.OST_RasterImages:
                return true;
            default:
                return false;
        }
    }

    private static void AccumulateOrphan(Element element, Dictionary<string, OrphanGroup> bucket)
    {
        var catName = element.Category?.Name ?? "(sin categoria)";
        var typeName = GetTypeName(element);
        var key = catName + "|" + typeName;

        if (!bucket.TryGetValue(key, out var grp))
        {
            grp = new OrphanGroup
            {
                CategoryDisplayName = catName,
                TypeOrFamilyName = typeName,
            };
            bucket[key] = grp;
        }
        grp.Count++;
        grp.ElementIds.Add(element.Id.Value);
    }

    private static string GetTypeName(Element element)
    {
        try
        {
            var typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var typeEl = element.Document.GetElement(typeId);
                if (typeEl is ElementType et) return et.Name;
                if (typeEl is not null) return typeEl.Name;
            }
        }
        catch { /* fallback abajo */ }
        return element.Name ?? "(sin tipo)";
    }

    /// <summary>
    /// Lee la medicion bruta del elemento en unidades internas Revit (feet).
    /// </summary>
    private static double ComputeMeasurement(Element element, MeasurementParameter measurement)
    {
        if (measurement is null) return 0;

        switch (measurement.BuiltIn)
        {
            case BuiltInMeasurement.Count:
                return 1.0;

            case BuiltInMeasurement.Length:
                return ReadLengthInternal(element);

            case BuiltInMeasurement.Area:
                return ReadAreaInternal(element);

            case BuiltInMeasurement.Volume:
                return ReadVolumeInternal(element);

            case BuiltInMeasurement.None:
                return ReadCustomParameter(element, measurement.CustomParameterName);

            default:
                return 0;
        }
    }

    private static double ReadLengthInternal(Element element)
    {
        var p = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
        if (p is not null && p.HasValue && p.StorageType == StorageType.Double)
            return p.AsDouble();

        if (element.Location is LocationCurve lc && lc.Curve is not null)
        {
            try { return lc.Curve.Length; } catch { /* sigue */ }
        }

        foreach (var name in new[] { "Longitud", "Length", "Largo" })
        {
            var pp = element.LookupParameter(name);
            if (pp is not null && pp.HasValue && pp.StorageType == StorageType.Double)
                return pp.AsDouble();
        }

        return 0;
    }

    private static double ReadAreaInternal(Element element)
    {
        var p = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        if (p is not null && p.HasValue && p.StorageType == StorageType.Double)
            return p.AsDouble();

        var bp = element.get_Parameter(BuiltInParameter.RBS_CURVE_SURFACE_AREA);
        if (bp is not null && bp.HasValue && bp.StorageType == StorageType.Double)
            return bp.AsDouble();

        foreach (var name in new[] { "Area", "Área", "Superficie" })
        {
            var pp = element.LookupParameter(name);
            if (pp is not null && pp.HasValue && pp.StorageType == StorageType.Double)
                return pp.AsDouble();
        }

        return 0;
    }

    private static double ReadVolumeInternal(Element element)
    {
        var p = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
        if (p is not null && p.HasValue && p.StorageType == StorageType.Double)
            return p.AsDouble();

        foreach (var name in new[] { "Volumen", "Volume" })
        {
            var pp = element.LookupParameter(name);
            if (pp is not null && pp.HasValue && pp.StorageType == StorageType.Double)
                return pp.AsDouble();
        }

        return 0;
    }

    private static double ReadCustomParameter(Element element, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return 0;

        var p = element.LookupParameter(parameterName);
        if (p is not null && p.HasValue)
        {
            if (p.StorageType == StorageType.Double) return p.AsDouble();
            if (p.StorageType == StorageType.Integer) return p.AsInteger();
        }

        var typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeEl = element.Document.GetElement(typeId);
            if (typeEl is not null)
            {
                var tp = typeEl.LookupParameter(parameterName);
                if (tp is not null && tp.HasValue)
                {
                    if (tp.StorageType == StorageType.Double) return tp.AsDouble();
                    if (tp.StorageType == StorageType.Integer) return tp.AsInteger();
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Convierte el valor desde unidades internas de Revit (feet) a metricas.
    /// Para Count o Custom, devuelve el valor tal cual (sin convertir).
    /// </summary>
    private static double ToMetric(double internalValue, MeasurementParameter measurement)
    {
        try
        {
            return measurement.BuiltIn switch
            {
                BuiltInMeasurement.Length => UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Meters),
                BuiltInMeasurement.Area   => UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.SquareMeters),
                BuiltInMeasurement.Volume => UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.CubicMeters),
                _ => internalValue,
            };
        }
        catch
        {
            return measurement.BuiltIn switch
            {
                BuiltInMeasurement.Length => internalValue * 0.3048,
                BuiltInMeasurement.Area   => internalValue * 0.092903,
                BuiltInMeasurement.Volume => internalValue * 0.028316846592,
                _ => internalValue,
            };
        }
    }

    private static string DefaultUnitFor(BuiltInMeasurement m) => m switch
    {
        BuiltInMeasurement.Length => "m",
        BuiltInMeasurement.Area   => "m2",
        BuiltInMeasurement.Volume => "m3",
        BuiltInMeasurement.Count  => "ud",
        _ => string.Empty,
    };
}
