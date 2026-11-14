// ============================================================================
//  PipeNetworkReader.cs
//  RevitBridge - Lee un PipingSystem de Revit y construye un PipeNetwork.
//
//  Briefing tecnico v1.0 - Mayo 2026, seccion 3.1.
//
//  Topologia: Union-Find sobre Connector.AllRefs (filtrando por IsConnectedTo).
//  Fittings/accessories tratados como nodo unico.  Pipe y FlexPipe son aristas.
//  Demandas: lee Flow_Cold / Flow_Hot de los OST_PlumbingFixtures conectados.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;
using PipeSegment = e3gbimtools.Calc.Core.Hydraulics.PipeSegment;

namespace e3gbimtools.Calc.RevitBridge
{
    public class PipeNetworkReader
    {
        public double XyzToleranceM { get; set; } = 0.005;

        /// <summary>
        /// Nombre del parametro de la familia del aparato sanitario que
        /// contiene el caudal de demanda.  Si es null, se autodetecta a
        /// partir de la SystemClassification: ACS -> Flow_Hot, resto -> Flow_Cold.
        /// </summary>
        public string DemandParameterName { get; set; }

        public List<CalcWarning> Messages { get; } = new List<CalcWarning>();

        public PipeNetwork ReadFromSystem(Document doc, PipingSystem system)
        {
            if (doc == null)    throw new ArgumentNullException(nameof(doc));
            if (system == null) throw new ArgumentNullException(nameof(system));

            var network = new PipeNetwork { SystemName = system.Name };

            var pipes = GetPipesInSystem(doc, system);
            if (pipes.Count == 0)
            {
                Messages.Add(CalcWarning.Warn("EMPTY_SYSTEM",
                    "El sistema '" + system.Name + "' no contiene tramos de tuberia."));
                return network;
            }

            // -----------------------------------------------------------------
            //  Step 1: Union-Find sobre todos los connectors del sistema
            // -----------------------------------------------------------------
            var uf = new ConnectorUnionFind();
            int unionedFittings = 0;

            var allElements = new List<Element>();
            var pn = system.PipingNetwork;
            if (pn != null)
            {
                foreach (Element e in pn) allElements.Add(e);
            }

            foreach (var elem in allElements)
            {
                var endConns = GetEndConnectors(elem);
                if (endConns.Count == 0) continue;

                long ownerKey = elem.Id.Value;
                foreach (var c in endConns)
                {
                    long key = ConnKey(c);
                    uf.Add(key);
                    try
                    {
                        if (c.IsConnected)
                        {
                            foreach (Connector other in c.AllRefs)
                            {
                                if (other == null) continue;
                                if (other.ConnectorType != ConnectorType.End) continue;
                                if (other.Owner == null) continue;
                                if (other.Owner.Id.Value == ownerKey) continue;
                                bool physical = false;
                                try { physical = c.IsConnectedTo(other); }
                                catch { physical = false; }
                                if (!physical) continue;
                                uf.Union(key, ConnKey(other));
                            }
                        }
                    }
                    catch { }
                }

                bool isCurveBased = elem is Pipe || elem is FlexPipe;
                if (!isCurveBased && endConns.Count >= 2)
                {
                    long firstKey = ConnKey(endConns[0]);
                    for (int i = 1; i < endConns.Count; i++)
                        uf.Union(firstKey, ConnKey(endConns[i]));
                    unionedFittings++;
                }
            }

            // -----------------------------------------------------------------
            //  Step 2: Construir segmentos (los Pipe son las aristas)
            // -----------------------------------------------------------------
            var rootToNode = new Dictionary<long, PipeNode>();

            PipeNode GetNode(Connector c)
            {
                long root = uf.Find(ConnKey(c));
                if (rootToNode.TryGetValue(root, out var n)) return n;
                var node = network.CreateNode(RevitUnits.LengthToSI(c.Origin.Z));
                node.X = RevitUnits.LengthToSI(c.Origin.X);
                node.Y = RevitUnits.LengthToSI(c.Origin.Y);
                rootToNode[root] = node;
                return node;
            }

            foreach (var pipe in pipes)
            {
                try
                {
                    var seg = BuildSegmentFromPipe(pipe);
                    if (seg == null) continue;

                    var endConns = GetEndConnectors(pipe);
                    if (endConns.Count != 2) continue;

                    seg.NodeStart = GetNode(endConns[0]);
                    seg.NodeEnd   = GetNode(endConns[1]);
                    if (ReferenceEquals(seg.NodeStart, seg.NodeEnd))
                    {
                        Messages.Add(CalcWarning.Warn("SELF_LOOP",
                            "Pipe " + pipe.Id.Value + " conecta nodo consigo mismo - omitido.",
                            pipe.Id.Value));
                        continue;
                    }
                    network.AddSegment(seg);
                }
                catch (Exception ex)
                {
                    Messages.Add(CalcWarning.Warn("READ_PIPE",
                        "Error leyendo pipe Id=" + pipe.Id.Value + ": " + ex.Message,
                        pipe.Id.Value));
                }
            }

            Messages.Add(CalcWarning.Info("TOPO_DIAG",
                "Topologia construida: " + network.Nodes.Count + " nodos, "
                + network.Segments.Count + " segs (de " + pipes.Count + " pipes), "
                + unionedFittings + " fittings/accessories/fixtures fusionados como nodo."));

            foreach (var n in network.Nodes)
            {
                if (n.ConnectedSegments.Count == 1)
                {
                    n.IsTerminal = true;
                    if (!network.TerminalNodes.Contains(n))
                        network.TerminalNodes.Add(n);
                }
            }

            // -----------------------------------------------------------------
            //  Step 3: Demandas de los aparatos sanitarios (via Union-Find)
            // -----------------------------------------------------------------
            AssignDemandsFromFixtures(doc, system, network, uf, rootToNode);

            // -----------------------------------------------------------------
            //  Step 4: Detectar fuente desde MechanicalEquipment / PipeAccessory
            // -----------------------------------------------------------------
            TryAssignSourceFromEquipment(doc, system, network, uf, rootToNode);

            return network;
        }

        private void TryAssignSourceFromEquipment(Document doc, PipingSystem system,
            PipeNetwork network, ConnectorUnionFind uf, Dictionary<long, PipeNode> rootToNode)
        {
            var net = system.PipingNetwork;
            if (net == null) return;

            FamilyInstance bestEq = null;
            PipeNode bestNode = null;

            foreach (Element e in net)
            {
                if (!(e is FamilyInstance fi)) continue;
                if (fi.Category == null) continue;
                long catId = fi.Category.Id.Value;
                bool isCandidate =
                       catId == (long)BuiltInCategory.OST_MechanicalEquipment
                    || catId == (long)BuiltInCategory.OST_PipeAccessory;
                if (!isCandidate) continue;

                var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) continue;

                foreach (Connector c in conns)
                {
                    if (c.Domain != Domain.DomainPiping) continue;
                    if (c.ConnectorType != ConnectorType.End) continue;
                    bool isOut = false;
                    try { isOut = c.Direction == FlowDirectionType.Out; }
                    catch { isOut = false; }
                    if (!isOut) continue;
                    long root = uf.Find(ConnKey(c));
                    if (rootToNode.TryGetValue(root, out var node))
                    {
                        bestEq = fi;
                        bestNode = node;
                        break;
                    }
                }
                if (bestNode != null) break;
            }

            if (bestNode == null)
            {
                Messages.Add(CalcWarning.Info("NO_SOURCE_EQUIPMENT",
                    "No se ha detectado MechanicalEquipment/PipeAccessory con connector Out. "
                    + "Se usara fallback por cota."));
                return;
            }

            bestNode.IsSource = true;
            bestNode.IsTerminal = false;
            if (network.TerminalNodes.Contains(bestNode))
                network.TerminalNodes.Remove(bestNode);
            if (network.SourceNode == null) network.SourceNode = bestNode;
            if (string.IsNullOrEmpty(bestNode.Label)) bestNode.Label = bestEq.Name;
            Messages.Add(CalcWarning.Warn("SOURCE_OK",
                "Fuente detectada: equipo '" + bestEq.Name + "' (Out connector) -> nodo N"
                + bestNode.Id + "."));
        }

        private void AssignDemandsFromFixtures(Document doc, PipingSystem system,
            PipeNetwork network, ConnectorUnionFind uf, Dictionary<long, PipeNode> rootToNode)
        {
            string paramName = DemandParameterName ?? DetectDemandParameterName(doc, system);

            var fixtures = new List<FamilyInstance>();
            var net = system.PipingNetwork;
            if (net != null)
            {
                foreach (Element e in net)
                {
                    if (e is FamilyInstance fi
                        && fi.Category != null
                        && fi.Category.Id.Value == (long)BuiltInCategory.OST_PlumbingFixtures)
                    {
                        fixtures.Add(fi);
                    }
                }
            }

            if (fixtures.Count == 0)
            {
                Messages.Add(CalcWarning.Warn("NO_FIXTURES",
                    "No se han detectado aparatos sanitarios conectados al sistema. "
                    + "Los caudales calculados seran nulos."));
                return;
            }

            int assigned = 0;
            var diag = new System.Text.StringBuilder();
            int diagCount = 0;

            foreach (var fix in fixtures)
            {
                try
                {
                    var p = fix.LookupParameter(paramName);
                    if (p == null || !p.HasValue) continue;
                    double demandM3s = ReadFixtureDemand(p);
                    if (diagCount < 8)
                    {
                        var fam = (fix.Document.GetElement(fix.GetTypeId()) as ElementType)?.FamilyName ?? "?";
                        var typ = (fix.Document.GetElement(fix.GetTypeId()) as ElementType)?.Name ?? "?";
                        diag.Append("[").Append(fam).Append("/").Append(typ).Append(" Q=")
                            .Append((demandM3s * 1000.0).ToString("0.000")).Append(" l/s] ");
                        diagCount++;
                    }
                    if (demandM3s <= 0.0) continue;

                    var conns = fix.MEPModel?.ConnectorManager?.Connectors;
                    if (conns == null) continue;

                    foreach (Connector c in conns)
                    {
                        if (c.Domain != Domain.DomainPiping) continue;
                        if (c.ConnectorType != ConnectorType.End) continue;
                        long root = uf.Find(ConnKey(c));
                        if (!rootToNode.TryGetValue(root, out var node)) continue;
                        node.DemandM3s += demandM3s;
                        node.IsTerminal = true;
                        if (!network.TerminalNodes.Contains(node))
                            network.TerminalNodes.Add(node);
                        if (string.IsNullOrEmpty(node.Label)) node.Label = fix.Name;
                        assigned++;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Messages.Add(CalcWarning.Warn("READ_FIXTURE",
                        "Error leyendo aparato Id=" + fix.Id.Value + ": " + ex.Message,
                        fix.Id.Value));
                }
            }

            if (assigned == 0)
            {
                Messages.Add(CalcWarning.Warn("NO_DEMANDS",
                    "Detectados " + fixtures.Count + " aparatos en el sistema, pero "
                    + "ninguno aporta demanda > 0 en el parametro '" + paramName + "'. "
                    + "Verifica que la familia tiene ese parametro y que el valor no es 0."));
            }
            else
            {
                Messages.Add(CalcWarning.Info("DEMANDS_OK",
                    "Asignadas demandas de " + assigned + " aparato(s) usando '" + paramName + "'."));
                if (diag.Length > 0)
                    Messages.Add(CalcWarning.Info("DEMANDS_DIAG",
                        "Muestra (familia/tipo Q): " + diag.ToString()));
            }
        }

        private static double ReadFixtureDemand(Parameter p)
        {
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return 0.0;
            double raw = p.AsDouble();
            try
            {
                var unitId = p.GetUnitTypeId();
                if (unitId != null && !unitId.Empty())
                    return RevitUnits.FlowToSI(raw);
            }
            catch { }
            return raw / 1000.0;
        }

        private static string DetectDemandParameterName(Document doc, PipingSystem system)
        {
            try
            {
                if (doc.GetElement(system.GetTypeId()) is PipingSystemType pst)
                {
                    if (pst.SystemClassification == MEPSystemClassification.DomesticHotWater)
                        return "Flow_Hot";
                }
            }
            catch { }
            return "Flow_Cold";
        }

        private static PipeSegment BuildSegmentFromPipe(Pipe pipe)
        {
            double dM   = RevitUnits.GetLengthSI(pipe, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) ?? 0.0;
            double lenM = RevitUnits.GetLengthSI(pipe, BuiltInParameter.CURVE_ELEM_LENGTH) ?? 0.0;
            double qM3s = RevitUnits.GetFlowSI(pipe, BuiltInParameter.RBS_PIPE_FLOW_PARAM) ?? 0.0;

            string materialKey = ResolveMaterial(pipe);
            string sysType     = ReadSystemType(pipe);
            string levelName   = ReadLevelName(pipe);

            return new PipeSegment
            {
                RevitId     = pipe.Id.Value,
                DiameterM   = dM,
                LengthM     = lenM,
                FlowM3s     = qM3s,
                MaterialKey = materialKey,
                Roughness   = MaterialDatabase.GetRoughness(materialKey),
                SystemType  = sysType,
                LevelName   = levelName,
                CatalogSeries = ReadCatalogSeries(pipe),
            };
        }

        /// <summary>
        /// Lee las tallas comerciales del tipo de tubería en Revit
        /// (RoutingPreferenceManager → reglas de Segments → PipeSegment.GetSizes()).
        /// Devuelve una serie ordenada asc. por diámetro nominal, o null si el
        /// tipo no expone catálogo.  Diámetros convertidos de pies a metros.
        /// </summary>
        private static IReadOnlyList<NominalDiameter> ReadCatalogSeries(Pipe pipe)
        {
            const double FeetToM = 0.3048;
            try
            {
                var pipeType = pipe.PipeType;
                var rpm = pipeType != null ? pipeType.RoutingPreferenceManager : null;
                if (rpm == null) return null;

                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                var segIds = new HashSet<ElementId>();
                for (int i = 0; i < n; i++)
                {
                    var rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                    if (rule != null && rule.MEPPartId != ElementId.InvalidElementId)
                        segIds.Add(rule.MEPPartId);
                }

                var byNominalMm = new Dictionary<int, NominalDiameter>();
                foreach (var segId in segIds)
                {
                    var seg = pipe.Document.GetElement(segId) as Autodesk.Revit.DB.Plumbing.PipeSegment;
                    if (seg == null) continue;
                    foreach (var sz in seg.GetSizes())
                    {
                        double nomM   = sz.NominalDiameter * FeetToM;
                        double innerM = sz.InnerDiameter   * FeetToM;
                        double outerM = sz.OuterDiameter   * FeetToM;
                        if (innerM <= 0.0) continue;
                        int key = (int)System.Math.Round(nomM * 1000.0);
                        if (byNominalMm.ContainsKey(key)) continue;
                        byNominalMm[key] = new NominalDiameter
                        {
                            Label          = "DN" + key,
                            Dn             = key,
                            InnerDiameterM = innerM,
                            OuterDiameterM = outerM,
                            Series         = "REVIT_CATALOG",
                        };
                    }
                }

                if (byNominalMm.Count == 0) return null;
                return byNominalMm.Values.OrderBy(nd => nd.Dn).ToList();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadLevelName(Pipe pipe)
        {
            try
            {
                if (pipe.ReferenceLevel != null && !string.IsNullOrEmpty(pipe.ReferenceLevel.Name))
                    return pipe.ReferenceLevel.Name;
                var p = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (p != null && p.HasValue) return p.AsValueString() ?? p.AsString() ?? "";
            }
            catch { }
            return "";
        }

        private static string ResolveMaterial(Pipe pipe)
        {
            try
            {
                if (pipe.Document.GetElement(pipe.GetTypeId()) is PipeType)
                {
                    var matIdParam = pipe.LookupParameter("Material");
                    if (matIdParam != null && matIdParam.HasValue)
                    {
                        var matName = matIdParam.AsValueString() ?? matIdParam.AsString();
                        if (!string.IsNullOrEmpty(matName))
                            return MaterialDatabase.ResolveFromRevitName(matName);
                    }
                }
            }
            catch { }
            return "STEEL_GALV";
        }

        private static string ReadSystemType(Pipe pipe)
        {
            try
            {
                var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                if (p != null && p.HasValue) return p.AsValueString() ?? p.AsString();
            }
            catch { }
            return null;
        }

        private static List<Pipe> GetPipesInSystem(Document doc, PipingSystem system)
        {
            var result = new List<Pipe>();
            var network = system.PipingNetwork;
            if (network == null) return result;
            foreach (Element e in network)
            {
                if (e is Pipe p) result.Add(p);
            }
            return result;
        }

        private static List<Connector> GetEndConnectors(Element e)
        {
            var result = new List<Connector>();
            ConnectorManager cm = null;
            if (e is Pipe p) cm = p.ConnectorManager;
            else if (e is FlexPipe fp) cm = fp.ConnectorManager;
            else if (e is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
            if (cm == null) return result;
            foreach (Connector c in cm.Connectors)
            {
                if (c.ConnectorType == ConnectorType.End) result.Add(c);
            }
            return result;
        }

        private static long ConnKey(Connector c)
        {
            if (c == null || c.Owner == null) return 0L;
            return c.Owner.Id.Value * 1_000_000L + c.Id;
        }

        private class ConnectorUnionFind
        {
            private readonly Dictionary<long, long> _parent = new Dictionary<long, long>();

            public void Add(long x)
            {
                if (!_parent.ContainsKey(x)) _parent[x] = x;
            }

            public long Find(long x)
            {
                if (!_parent.ContainsKey(x)) { _parent[x] = x; return x; }
                long p = _parent[x];
                if (p != x) { p = Find(p); _parent[x] = p; }
                return p;
            }

            public void Union(long a, long b)
            {
                long ra = Find(a);
                long rb = Find(b);
                if (ra != rb) _parent[ra] = rb;
            }
        }
    }
}
