using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;

namespace E3G.BimTools.DiTools.Core.Services.RefrigerantRouting;

/// <summary>
/// Implementacion Revit de <see cref="IRefrigerantRoutingService"/>.  La geometria
/// (lectura, encaje y plan) es pura y vive en <see cref="LatsPlanReader"/>,
/// <see cref="PlanAligner"/> y <see cref="RefrigerantPlanner"/>; aqui solo se
/// lee el proyecto y se crean los elementos.
///
/// Cada elemento creado lleva en Comentarios «E3G_REF &lt;archivo&gt; &lt;handle&gt; &lt;tubo&gt;»:
/// con «Sustituir lo anterior» se borra lo modelado antes desde ese mismo DXF.
/// </summary>
public sealed class RefrigerantRoutingService : IRefrigerantRoutingService
{
    private const double Ft = 1 / 0.3048;
    private const double CutHeightM = 1.2;
    private static readonly Color LiquidColor = new(191, 255, 127);
    private static readonly Color GasColor = new(153, 76, 76);

    private readonly ILogger _log;

    public RefrigerantRoutingService(ILoggerFactory loggerFactory) =>
        _log = loggerFactory?.CreateLogger<RefrigerantRoutingService>() ?? throw new ArgumentNullException(nameof(loggerFactory));

    public LatsPlan Read(string path, LatsReadOptions options) => new LatsPlanReader(options).Read(path);

    public PlanTransform? Align(LatsPlan plan, IReadOnlyList<Seg2> revit) => PlanAligner.Align(plan, revit);

    public PlanTransform Evaluate(PlanTransform transform, LatsPlan plan, IReadOnlyList<Seg2> revit) =>
        PlanAligner.Evaluate(transform, plan.Architecture, revit);

    public RefrigerantRoutingPlan BuildPlan(LatsPlan plan, PlanTransform transform, RefrigerantRoutingOptions options) =>
        RefrigerantPlanner.Build(plan, transform, options);

    // ============================================================
    // Proyecto
    // ============================================================

    public RefrigerantProjectInfo GetProjectInfo(Document doc, View? activeView)
    {
        var views = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Where(v => !v.IsTemplate && v.GenLevel is not null
                        && v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan)
            .Select(v => new RefPlanView(v.Id, v.Name, v.GenLevel.Id, v.GenLevel.Name))
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => new RefLevel(l.Id, l.Name, l.Elevation / Ft))
            .ToList();
        var pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>()
            .Select(t => new RefNamedId(t.Id, t.Name)).OrderBy(t => t.Name).ToList();
        var systems = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>()
            .Select(t => new RefNamedId(t.Id, t.Name)).OrderBy(t => t.Name).ToList();
        var accessories = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_PipeAccessory).Cast<FamilySymbol>()
            .Select(s => new RefNamedId(s.Id, s.FamilyName + " : " + s.Name)).OrderBy(s => s.Name).ToList();
        return new RefrigerantProjectInfo
        {
            Views = views, Levels = levels, PipeTypes = pipeTypes, PipingSystems = systems, AccessoryTypes = accessories,
            ActiveViewId = activeView?.Id ?? ElementId.InvalidElementId,
        };
    }

    public IReadOnlyList<Seg2> GetArchitecture(Document doc, long levelId)
    {
        if (doc.GetElement(new ElementId(levelId)) is not Level level) return Array.Empty<Seg2>();
        double cut = level.Elevation + CutHeightM * Ft;
        var segs = new List<Seg2>();
        AddWalls(doc, Transform.Identity, cut, segs);
        foreach (var li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            var ld = li.GetLinkDocument();
            if (ld is null) continue;
            AddWalls(ld, li.GetTotalTransform(), cut, segs);
        }
        _log.Information("Arquitectura del nivel {0}: {1} caras de muro", level.Name, segs.Count);
        return segs;
    }

    /// <summary>Caras de los muros cortados a la cota <paramref name="cut"/> (pies, coordenadas del anfitrion).</summary>
    private static void AddWalls(Document d, Transform tf, double cut, List<Seg2> segs)
    {
        foreach (var w in new FilteredElementCollector(d).OfClass(typeof(Wall)).Cast<Wall>())
        {
            var bb = w.get_BoundingBox(null);
            if (bb is null) continue;
            double z0 = tf.OfPoint(bb.Min).Z, z1 = tf.OfPoint(bb.Max).Z;
            if (Math.Min(z0, z1) > cut || Math.Max(z0, z1) < cut) continue;
            if (w.Location is not LocationCurve lc) continue;

            var pts = lc.Curve is Line ? new List<XYZ> { lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1) } : lc.Curve.Tessellate().ToList();
            pts = pts.Select(tf.OfPoint).ToList();
            double half = w.Width / 2;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = new P2(pts[i - 1].X / Ft, pts[i - 1].Y / Ft);
                var b = new P2(pts[i].X / Ft, pts[i].Y / Ft);
                var n = (b - a).Normalized().Left * (half / Ft);
                if (half * 304.8 < 30) segs.Add(new Seg2(a, b));
                else { segs.Add(new Seg2(a + n, b + n)); segs.Add(new Seg2(a - n, b - n)); }
            }
        }
    }

    // ============================================================
    // Modelado
    // ============================================================

    public RefrigerantRoutingResult Apply(Document doc, LatsPlan plan, PlanTransform transform, RefrigerantRoutingPlan routing, RefrigerantRoutingOptions o)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>(routing.Warnings);
        if (doc.GetElement(new ElementId(o.LevelId)) is not Level level)
            return Fail("Elige el nivel asociado.");
        var pipeType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>()
            .FirstOrDefault(t => t.Name.Equals(o.PipeTypeName, StringComparison.OrdinalIgnoreCase));
        if (pipeType is null) return Fail($"No existe el tipo de tubería «{o.PipeTypeName}».");

        string file = Path.GetFileName(plan.FilePath);
        string tagBase = $"{o.TagPrefix} {file}";
        double z = level.Elevation + o.Height * Ft;

        using var group = new TransactionGroup(doc, "Trazado de refrigerante desde DXF");
        group.Start();
        int deleted = 0, familiesCreated = 0;
        ElementId liquidSys, gasSys;
        var symbols = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
        var sizes = new Dictionary<double, double>(); // OD mm -> diametro nominal (pies)

        // 1) Preparar: borrar lo anterior, sistemas, diametros y familias.
        using (var tx = new Transaction(doc, "Preparar trazado de refrigerante"))
        {
            tx.Start();
            Silence(tx);
            try
            {
                if (o.ReplacePrevious) deleted = DeletePrevious(doc, tagBase);

                liquidSys = EnsureSystem(doc, o.LiquidSystemName, LiquidColor, "LIQ", o.CreateMissingSystems, warnings);
                gasSys = EnsureSystem(doc, o.GasSystemName, GasColor, "GAS", o.CreateMissingSystems, warnings);
                if (liquidSys == ElementId.InvalidElementId || gasSys == ElementId.InvalidElementId)
                {
                    tx.RollBack(); group.RollBack();
                    return Fail("Faltan los sistemas de tubería (actívalo en Ajustes para crearlos).");
                }

                var segment = SegmentOf(pipeType);
                foreach (double od in routing.Pipes.Select(p => Math.Round(p.OuterDiameterMm, 2)).Where(d => d > 0).Distinct())
                    sizes[od] = NominalFor(segment, od, o.AddMissingSizes, warnings);

                foreach (var blockName in routing.Devices.Select(d => d.BlockName).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var dev = routing.Devices.First(d => d.BlockName.Equals(blockName, StringComparison.OrdinalIgnoreCase));
                    o.Mappings.TryGetValue(blockName, out var map);
                    FamilySymbol? sym = map?.SymbolId is long id ? doc.GetElement(new ElementId(id)) as FamilySymbol : null;
                    if (sym is null)
                    {
                        sym = ExistingSymbol(doc, blockName);
                        if (sym is null && plan.Blocks.TryGetValue(blockName, out var def))
                        {
                            var lats = plan.Devices.First(d => d.BlockName.Equals(blockName, StringComparison.OrdinalIgnoreCase));
                            double k = Math.Sqrt(Math.Abs(lats.A * lats.D - lats.B * lats.C)) * transform.Scale;
                            // Si no se puede fabricar (p. ej. falta la plantilla) se omite ese
                            // accesorio y se modela el resto, en vez de cancelarlo todo.
                            try
                            {
                                sym = AccessoryFamilyFactory.Create(doc, def, dev.Role, k, o.Spacing, o.UnitHeight, warnings);
                                familiesCreated++;
                            }
                            catch (Exception ex)
                            {
                                _log.Warning("No se pudo crear la familia {0}: {1}", blockName, ex.Message);
                                warnings.Add($"No se pudo crear la familia «{blockName}» ({ex.Message}).");
                                sym = null;
                            }
                        }
                    }
                    if (sym is null)
                    {
                        int n = routing.Devices.Count(d => d.BlockName.Equals(blockName, StringComparison.OrdinalIgnoreCase));
                        warnings.Add($"Sin familia para «{blockName}»: no se colocan sus {n} accesorios (las tuberías sí se modelan).");
                        continue;
                    }
                    if (!sym.IsActive) sym.Activate();
                    symbols[blockName] = sym;
                }
                doc.Regenerate();
                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                group.RollBack();
                _log.Error(ex, "Preparar trazado de refrigerante");
                return Fail(ex.Message);
            }
        }

        // 2) Accesorios, tuberias, codos y conexiones.
        int nPipes = 0, nElbows = 0, nAcc = 0, nConn = 0;
        double length = 0;
        using (var tx = new Transaction(doc, "Modelar trazado de refrigerante"))
        {
            tx.Start();
            var swallowed = Silence(tx);
            try
            {
                var branches = new Dictionary<(string, int), FamilyInstance>();
                foreach (var d in routing.Devices)
                {
                    if (!symbols.TryGetValue(d.BlockName, out var sym)) continue;
                    var fi = Place(doc, sym, level, d, z, o.Height);
                    SetComment(fi, $"{tagBase} {d.Handle}" + (d.Line is int ln ? " " + LineTag(ln) : string.Empty));
                    foreach (var kv in d.PortDiametersMm)
                    {
                        var p = fi.LookupParameter("D_" + (kv.Key + 1));
                        double od = Math.Round(kv.Value, 2);
                        if (p is { IsReadOnly: false } && p.StorageType == StorageType.Double)
                            p.Set(sizes.TryGetValue(od, out var nom) ? nom : kv.Value / 304.8);
                    }
                    if (d.Role == BlockRole.Branch && d.Line is int line) branches[(d.Handle, line)] = fi;
                    nAcc++;
                }
                doc.Regenerate();

                foreach (var rp in routing.Pipes)
                {
                    var pts = rp.Points.Select(p => new XYZ(p.X * Ft, p.Y * Ft, z)).ToList();
                    // Extremos en un derivador: al conector real (la familia puede no coincidir con el bloque).
                    Connector? startConn = SnapEnd(branches, rp.StartPort, rp.Line, pts, true);
                    Connector? endConn = SnapEnd(branches, rp.EndPort, rp.Line, pts, false);

                    var pipes = new List<Pipe>();
                    for (int i = 1; i < pts.Count; i++)
                    {
                        if (pts[i].DistanceTo(pts[i - 1]) < 0.005 * Ft) continue;
                        var pipe = Pipe.Create(doc, rp.IsLiquid ? liquidSys : gasSys, pipeType.Id, level.Id, pts[i - 1], pts[i]);
                        double od = Math.Round(rp.OuterDiameterMm, 2);
                        if (sizes.TryGetValue(od, out var nominal)) pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(nominal);
                        SetComment(pipe, $"{tagBase} {rp.Source} {LineTag(rp.Line)}");
                        pipes.Add(pipe);
                        length += pts[i].DistanceTo(pts[i - 1]) / Ft;
                    }
                    nPipes += pipes.Count;
                    if (pipes.Count == 0) continue;
                    doc.Regenerate();

                    for (int i = 1; i < pipes.Count; i++)
                    {
                        var c1 = End(pipes[i - 1], false); var c2 = End(pipes[i], true);
                        if (c1 is null || c2 is null) continue;
                        try
                        {
                            var elbow = doc.Create.NewElbowFitting(c1, c2);
                            SetComment(elbow, $"{tagBase} {rp.Source} {LineTag(rp.Line)}");
                            nElbows++;
                        }
                        catch (Exception ex) { warnings.Add($"Tramo {rp.Source} ({LineTag(rp.Line)}): codo no creado ({ex.Message})."); }
                    }
                    if (startConn is not null && End(pipes[0], true) is { } a && TryConnect(a, startConn)) nConn++;
                    if (endConn is not null && End(pipes[^1], false) is { } b && TryConnect(b, endConn)) nConn++;
                }

                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                {
                    group.RollBack();
                    return Fail("Revit no ha aceptado el modelado (" + status + ").");
                }
                if (swallowed.Count > 0)
                    warnings.Add($"{swallowed.Count} avisos de Revit omitidos (p. ej. «{swallowed.First()}»).");
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                group.RollBack();
                _log.Error(ex, "Modelar trazado de refrigerante");
                return Fail(ex.Message);
            }
        }
        group.Assimilate();
        _log.Information("Trazado de refrigerante: {0} tuberías, {1} codos, {2} accesorios en {3} ms", nPipes, nElbows, nAcc, sw.ElapsedMilliseconds);

        return new RefrigerantRoutingResult
        {
            Success = true, Pipes = nPipes, Elbows = nElbows, Accessories = nAcc, Connections = nConn,
            Deleted = deleted, FamiliesCreated = familiesCreated, LengthM = length, Warnings = warnings,
        };

        RefrigerantRoutingResult Fail(string msg) => new() { Success = false, ErrorMessage = msg, Warnings = warnings };
    }

    private static string LineTag(int line) => line == 0 ? "L" : line == 1 ? "G" : "G" + line;

    private static void SetComment(Element e, string text)
    {
        var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (p is { IsReadOnly: false }) p.Set(text);
    }

    private static int DeletePrevious(Document doc, string tagBase)
    {
        var cats = new ElementMulticategoryFilter(new List<BuiltInCategory>
        {
            BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
        });
        var ids = new FilteredElementCollector(doc).WherePasses(cats).WhereElementIsNotElementType()
            .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() is string c
                        && c.StartsWith(tagBase + " ", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Id).ToList();
        if (ids.Count > 0) doc.Delete(ids);
        return ids.Count;
    }

    private static ElementId EnsureSystem(Document doc, string name, Color color, string abbreviation, bool create, List<string> warnings)
    {
        var all = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().ToList();
        var hit = all.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit.Id;
        if (!create) return ElementId.InvalidElementId;

        // Plantilla: uno de refrigerante del proyecto (no los que crea esta herramienta).
        var candidates = all.Where(s => !s.Name.StartsWith("50_1", StringComparison.OrdinalIgnoreCase)).ToList();
        var template = candidates.FirstOrDefault(s => s.Name.IndexOf("refriger", StringComparison.OrdinalIgnoreCase) >= 0
                                                      && s.Name.IndexOf(abbreviation == "GAS" ? "gas" : "liq", StringComparison.OrdinalIgnoreCase) >= 0)
                       ?? candidates.FirstOrDefault(s => s.Name.IndexOf("refriger", StringComparison.OrdinalIgnoreCase) >= 0)
                       ?? candidates.FirstOrDefault(s => s.SystemClassification == MEPSystemClassification.SupplyHydronic)
                       ?? all.FirstOrDefault();
        if (template is null) return ElementId.InvalidElementId;
        var st = (PipingSystemType)template.Duplicate(name);
        st.LineColor = color;
        var ab = st.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM);
        if (ab is { IsReadOnly: false }) ab.Set(abbreviation);
        warnings.Add($"Creado el tipo de sistema «{name}» (copia de «{template.Name}»).");
        return st.Id;
    }

    private static Segment? SegmentOf(PipeType type)
    {
        var rpm = type.RoutingPreferenceManager;
        for (int i = 0; i < rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments); i++)
            if (type.Document.GetElement(rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i).MEPPartId) is Segment s)
                return s;
        return null;
    }

    /// <summary>Diametro nominal (pies) del tamaño del segmento con ese diametro exterior; si falta, se añade o se toma el mas cercano.</summary>
    private static double NominalFor(Segment? segment, double odMm, bool add, List<string> warnings)
    {
        if (segment is null) return odMm / 304.8;
        var list = segment.GetSizes().ToList();
        var hit = list.FirstOrDefault(s => Math.Abs(s.OuterDiameter * 304.8 - odMm) < 0.35);
        if (hit is not null) return hit.NominalDiameter;
        if (add)
        {
            double nominal = Math.Round(odMm);
            if (list.Any(s => Math.Abs(s.NominalDiameter * 304.8 - nominal) < 0.01)) nominal = Math.Round(odMm, 1);
            // Pared del tamaño mas parecido, pero nunca mas del 12 % del diametro (el segmento puede ser de acero).
            var similar = list.OrderBy(s => Math.Abs(s.OuterDiameter * 304.8 - odMm)).FirstOrDefault();
            double wall = similar is null ? 0.8 : (similar.OuterDiameter - similar.InnerDiameter) / 2 * 304.8;
            wall = Math.Clamp(wall, 0.3, odMm * 0.12);
            segment.AddSize(new MEPSize(nominal / 304.8, (odMm - 2 * wall) / 304.8, odMm / 304.8, true, true));
            warnings.Add($"Añadido al segmento «{segment.Name}» el diámetro {nominal:0.#} (Ø ext. {odMm:0.##} mm).");
            return nominal / 304.8;
        }
        var near = list.OrderBy(s => Math.Abs(s.OuterDiameter * 304.8 - odMm)).FirstOrDefault();
        if (near is null) return odMm / 304.8;
        warnings.Add($"Ø ext. {odMm:0.##} mm no está en «{segment.Name}»: se usa {near.NominalDiameter * 304.8:0.#}.");
        return near.NominalDiameter;
    }

    private static FamilySymbol? ExistingSymbol(Document doc, string familyName) =>
        new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(s => s.FamilyName.Equals(AccessoryFamilyFactory.Sanitize(familyName), StringComparison.OrdinalIgnoreCase));

    private static FamilyInstance Place(Document doc, FamilySymbol sym, Level level, PlacedDevice d, double z, double heightM)
    {
        var pt = new XYZ(d.Origin.X * Ft, d.Origin.Y * Ft, level.Elevation);
        var fi = doc.Create.NewFamilyInstance(pt, sym, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
        var elev = fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM)
                   ?? fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
        if (elev is { IsReadOnly: false }) elev.Set(heightM * Ft);
        else if (fi.Location is LocationPoint lp) lp.Point = new XYZ(pt.X, pt.Y, z);

        var axisPt = new XYZ(pt.X, pt.Y, 0);
        if (Math.Abs(d.Rotation) > 1e-9)
            ElementTransformUtils.RotateElement(doc, fi.Id, Line.CreateBound(axisPt, axisPt + XYZ.BasisZ), d.Rotation);
        if (d.Mirrored)
            ElementTransformUtils.MirrorElements(doc, new[] { fi.Id },
                Plane.CreateByNormalAndOrigin(new XYZ(-Math.Sin(d.Rotation), Math.Cos(d.Rotation), 0), axisPt), false);
        return fi;
    }

    /// <summary>Lleva el extremo de la polilinea al conector mas cercano del derivador (hasta 30 cm).</summary>
    private static Connector? SnapEnd(Dictionary<(string, int), FamilyInstance> branches, (string Device, int Port)? end, int line, List<XYZ> pts, bool start)
    {
        if (end is not { } e || !branches.TryGetValue((e.Device, line), out var fi) || fi.MEPModel?.ConnectorManager is null) return null;
        var p = start ? pts[0] : pts[^1];
        Connector? best = null; double bd = 0.30 * Ft;
        foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
        {
            double d = c.Origin.DistanceTo(p);
            if (d < bd) { bd = d; best = c; }
        }
        if (best is null) return null;
        if (start) pts[0] = best.Origin; else pts[^1] = best.Origin;
        return best;
    }

    private static Connector? End(MEPCurve c, bool start)
    {
        var lc = (LocationCurve)c.Location;
        var p = lc.Curve.GetEndPoint(start ? 0 : 1);
        foreach (Connector k in c.ConnectorManager.Connectors)
            if (k.Origin.DistanceTo(p) < 1e-4) return k;
        return null;
    }

    private static bool TryConnect(Connector a, Connector b)
    {
        try
        {
            if (!a.IsConnectedTo(b)) a.ConnectTo(b);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Quita los avisos (sin dialogos) y los cuenta; los errores siguen cancelando.</summary>
    private static List<string> Silence(Transaction tx)
    {
        var swallowed = new List<string>();
        var opts = tx.GetFailureHandlingOptions();
        opts.SetFailuresPreprocessor(new WarningSwallower(swallowed));
        opts.SetClearAfterRollback(true);
        tx.SetFailureHandlingOptions(opts);
        return swallowed;
    }

    private sealed class WarningSwallower : IFailuresPreprocessor
    {
        private readonly List<string> _out;
        public WarningSwallower(List<string> output) => _out = output;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (var f in a.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    _out.Add(f.GetDescriptionText());
                    a.DeleteWarning(f);
                }
            return FailureProcessingResult.Continue;
        }
    }
}
