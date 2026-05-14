// ============================================================================
//  DuctNetworkReader.cs
//  RevitBridge - Lee un MechanicalSystem de Revit y construye un DuctNetwork.
//
//  Briefing tecnico v1.0 - Mayo 2026, seccion 4.1.
//
//  Topologia (Sprint 5 fix):  ya NO se fusionan connectors por proximidad XYZ.
//  En su lugar usamos Union-Find sobre `Connector.AllRefs` (la API de Revit
//  nos dice DIRECTAMENTE que connectors estan enlazados, sin depender de
//  posiciones XYZ que pueden no coincidir exactamente).  Las fittings se
//  tratan como "nodos topologicos" — todos sus connectors caen en el mismo
//  nodo logico, asi que cuando un Duct entra y otro sale, ambos comparten
//  ese nodo via la fitting.  Los Duct y FlexDuct siguen siendo aristas
//  (sus dos connectors son nodos distintos).
//
//  Demandas: las lee de los terminales de aire (OST_DuctTerminal) buscando
//  por nombre los parametros de caudal y perdida de carga.  Los nombres por
//  defecto cubren tanto familias en ingles como en espanol.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using E3G.Saneamiento.Core.Common;
using E3G.Saneamiento.Core.Aeraulics;
// Aliases: DuctShape colisiona con Autodesk.Revit.DB.Mechanical.DuctShape.
using DuctShape   = E3G.Saneamiento.Core.Aeraulics.DuctShape;
using DuctSegment = E3G.Saneamiento.Core.Aeraulics.DuctSegment;

namespace E3G.Saneamiento.RevitBridge
{
    /// <summary>
    /// Lectura de un sistema mecanico (HVAC) y construccion del DuctNetwork.
    /// </summary>
    public class DuctNetworkReader
    {
        /// <summary>Tolerancia residual usada solo si AllRefs no resuelve un connector.</summary>
        public double XyzToleranceM { get; set; } = 0.005;

        public List<CalcWarning> Messages { get; } = new List<CalcWarning>();

        /// <summary>Nombres del parametro de caudal a buscar en el terminal.</summary>
        public List<string> DemandParameterNames { get; set; } =
            new List<string> { "Flow", "Flujo" };

        /// <summary>Nombres del parametro de perdida de carga del terminal.</summary>
        public List<string> PressureDropParameterNames { get; set; } =
            new List<string> { "Pressure Drop", "Pressure_Drop", "Presure Drop", "Perdida de Carga", "Pérdida de Carga" };

        /// <summary>Lee un MechanicalSystem y devuelve el DuctNetwork resultante.</summary>
        public DuctNetwork ReadFromSystem(Document doc, MechanicalSystem system)
        {
            if (doc == null)    throw new ArgumentNullException(nameof(doc));
            if (system == null) throw new ArgumentNullException(nameof(system));

            var network = new DuctNetwork { SystemName = system.Name };

            var ducts = GetDuctsInSystem(doc, system);
            if (ducts.Count == 0)
            {
                Messages.Add(CalcWarning.Warn("EMPTY_SYSTEM",
                    "El sistema '" + system.Name + "' no contiene tramos de conducto."));
                return network;
            }

            // -----------------------------------------------------------------
            //  Step 1: Union-Find sobre todos los connectors del sistema
            // -----------------------------------------------------------------
            var uf = new ConnectorUnionFind();
            int unionedFittings = 0;

            var allElements = new List<Element>();
            var dn = system.DuctNetwork;
            if (dn != null)
            {
                foreach (Element e in dn) allElements.Add(e);
            }

            foreach (var elem in allElements)
            {
                var endConns = GetEndConnectors(elem);
                if (endConns.Count == 0) continue;

                // (a) Une cada connector unicamente con los que estan FISICAMENTE
                //     conectados (Connector.IsConnectedTo).  AllRefs en Revit
                //     incluye connectors del mismo MEPSystem que NO estan
                //     fisicamente conectados (refs logicas), lo que provocaria
                //     uniones espureas.  Filtrando por IsConnectedTo nos
                //     quedamos solo con vecinos reales.
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
                    catch { /* connector con AllRefs corrupto - skip */ }
                }

                // (b) Para elementos NO curve-based (fittings, accessories,
                //     terminals), unimos todos sus connectors entre si: una
                //     fitting es topologicamente UN nodo unico.
                bool isCurveBased = elem is Duct || elem is FlexDuct;
                if (!isCurveBased && endConns.Count >= 2)
                {
                    long firstKey = ConnKey(endConns[0]);
                    for (int i = 1; i < endConns.Count; i++)
                        uf.Union(firstKey, ConnKey(endConns[i]));
                    unionedFittings++;
                }
            }

            // -----------------------------------------------------------------
            //  Step 2: Construir segmentos a partir de los Duct (las aristas)
            // -----------------------------------------------------------------
            var rootToNode = new Dictionary<long, DuctNode>();

            DuctNode GetNode(Connector c)
            {
                long root = uf.Find(ConnKey(c));
                if (rootToNode.TryGetValue(root, out var n)) return n;
                var node = network.CreateNode(RevitUnits.LengthToSI(c.Origin.Z));
                rootToNode[root] = node;
                return node;
            }

            foreach (var duct in ducts)
            {
                try
                {
                    var seg = BuildSegmentFromDuct(duct);
                    if (seg == null) continue;

                    var endConns = GetEndConnectors(duct);
                    if (endConns.Count != 2) continue;

                    seg.NodeStart = GetNode(endConns[0]);
                    seg.NodeEnd   = GetNode(endConns[1]);
                    if (ReferenceEquals(seg.NodeStart, seg.NodeEnd))
                    {
                        // Duct cerrado sobre si mismo (fitting + duct ciclo) - omitir
                        Messages.Add(CalcWarning.Warn("SELF_LOOP",
                            "Duct " + duct.Id.Value + " conecta nodo consigo mismo - omitido.",
                            duct.Id.Value));
                        continue;
                    }
                    network.AddSegment(seg);
                }
                catch (Exception ex)
                {
                    Messages.Add(CalcWarning.Warn("READ_DUCT",
                        "Error leyendo duct Id=" + duct.Id.Value + ": " + ex.Message,
                        duct.Id.Value));
                }
            }

            Messages.Add(CalcWarning.Info("TOPO_DIAG",
                "Topologia construida: " + network.Nodes.Count + " nodos, "
                + network.Segments.Count + " segs (de " + ducts.Count + " ducts), "
                + unionedFittings + " fittings/accessories/terminals fusionados como nodo."));

            // Marcar terminales por grado (nodos con 1 unico tramo)
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
            //  Step 3: Demandas de los air terminals (via Union-Find)
            // -----------------------------------------------------------------
            AssignDemandsFromAirTerminals(doc, system, network, uf, rootToNode);

            // -----------------------------------------------------------------
            //  Step 4: Detectar fuente desde MechanicalEquipment (UTA / ventilador)
            //          Connector con FlowDirectionType.Out -> es la salida del equipo.
            // -----------------------------------------------------------------
            TryAssignSourceFromEquipment(doc, system, network, uf, rootToNode);

            return network;
        }

        /// <summary>
        /// Busca un MechanicalEquipment (UTA, ventilador) conectado al sistema
        /// y marca el nodo de su connector "Out" como SourceNode.  Si no se
        /// encuentra equipo, deja SourceNode == null para que el Cmd recurra
        /// a la heuristica de fallback (mayor cota).
        /// </summary>
        private void TryAssignSourceFromEquipment(Document doc, MechanicalSystem system,
            DuctNetwork network, ConnectorUnionFind uf, Dictionary<long, DuctNode> rootToNode)
        {
            var net = system.DuctNetwork;
            if (net == null) return;

            FamilyInstance bestEq = null;
            DuctNode bestNode = null;

            foreach (Element e in net)
            {
                if (!(e is FamilyInstance fi)) continue;
                if (fi.Category == null) continue;
                long catId = fi.Category.Id.Value;
                if (catId != (long)BuiltInCategory.OST_MechanicalEquipment) continue;

                var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) continue;

                foreach (Connector c in conns)
                {
                    if (c.Domain != Domain.DomainHvac) continue;
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
                    "No se ha detectado MechanicalEquipment con connector Out. "
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

        /// <summary>
        /// Lee terminales de aire del MechanicalSystem y suma su caudal al
        /// DuctNode correspondiente (resuelto via Union-Find por connector).
        /// </summary>
        private void AssignDemandsFromAirTerminals(Document doc, MechanicalSystem system,
            DuctNetwork network, ConnectorUnionFind uf, Dictionary<long, DuctNode> rootToNode)
        {
            var terminals = new List<FamilyInstance>();
            var net = system.DuctNetwork;
            if (net != null)
            {
                foreach (Element e in net)
                {
                    if (e is FamilyInstance fi
                        && fi.Category != null
                        && fi.Category.Id.Value == (long)BuiltInCategory.OST_DuctTerminal)
                    {
                        terminals.Add(fi);
                    }
                }
            }

            if (terminals.Count == 0)
            {
                Messages.Add(CalcWarning.Warn("NO_TERMINALS",
                    "No se han detectado terminales de aire conectados al sistema. "
                    + "Los caudales calculados seran nulos."));
                return;
            }

            int assigned = 0;
            double totalDpPa = 0.0;
            int dpRead = 0;
            var diag = new System.Text.StringBuilder();
            int diagCount = 0;

            foreach (var term in terminals)
            {
                try
                {
                    double demandM3s = ReadTerminalFlow(term);
                    if (diagCount < 8)
                    {
                        var fam = (term.Document.GetElement(term.GetTypeId()) as ElementType)?.FamilyName ?? "?";
                        var typ = (term.Document.GetElement(term.GetTypeId()) as ElementType)?.Name ?? "?";
                        diag.Append("[").Append(fam).Append("/").Append(typ).Append(" Q=")
                            .Append((demandM3s * 1000.0).ToString("0.000")).Append(" l/s] ");
                        diagCount++;
                    }
                    if (demandM3s <= 0.0) continue;

                    var conns = term.MEPModel?.ConnectorManager?.Connectors;
                    if (conns == null) continue;

                    DuctNode matchedNode = null;
                    foreach (Connector c in conns)
                    {
                        if (c.Domain != Domain.DomainHvac) continue;
                        if (c.ConnectorType != ConnectorType.End) continue;
                        long root = uf.Find(ConnKey(c));
                        if (rootToNode.TryGetValue(root, out var n))
                        {
                            matchedNode = n;
                            break;
                        }
                    }
                    if (matchedNode == null) continue;

                    matchedNode.DemandM3s += demandM3s;
                    matchedNode.IsTerminal = true;
                    if (!network.TerminalNodes.Contains(matchedNode))
                        network.TerminalNodes.Add(matchedNode);
                    if (string.IsNullOrEmpty(matchedNode.Label)) matchedNode.Label = term.Name;
                    assigned++;

                    double dpPa = ReadTerminalPressureDrop(term);
                    if (dpPa > 0.0)
                    {
                        matchedNode.TerminalPressureDropPa += dpPa;
                        totalDpPa += dpPa;
                        dpRead++;
                    }
                }
                catch (Exception ex)
                {
                    Messages.Add(CalcWarning.Warn("READ_TERMINAL",
                        "Error leyendo terminal Id=" + term.Id.Value + ": " + ex.Message,
                        term.Id.Value));
                }
            }

            if (assigned == 0)
            {
                Messages.Add(CalcWarning.Warn("NO_DEMANDS",
                    "Detectados " + terminals.Count + " terminales en el sistema, pero "
                    + "ninguno aporta caudal > 0 en los parametros buscados ("
                    + string.Join("/", DemandParameterNames) + " o RBS_DUCT_FLOW_PARAM)."));
            }
            else
            {
                Messages.Add(CalcWarning.Info("DEMANDS_OK",
                    "Asignadas demandas de " + assigned + " terminal(es) de aire."));
                if (diag.Length > 0)
                    Messages.Add(CalcWarning.Info("DEMANDS_DIAG",
                        "Muestra (familia/tipo Q): " + diag.ToString()));
                if (dpRead > 0)
                {
                    Messages.Add(CalcWarning.Info("TERMINAL_DP",
                        "Suma de perdidas de carga en " + dpRead + " terminal(es): "
                        + totalDpPa.ToString("0") + " Pa (sumadas al peor caso)."));
                }
            }
        }

        // -- Lectura de parametros de terminales -----------------------------

        private double ReadTerminalFlow(FamilyInstance term)
        {
            foreach (var name in DemandParameterNames)
            {
                var p = term.LookupParameter(name);
                if (p != null && p.HasValue)
                {
                    double v = ReadFlowValueSI(p);
                    if (v > 0.0) return v;
                }
            }
            try
            {
                var p = term.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                if (p != null && p.HasValue) return RevitUnits.FlowToSI(p.AsDouble());
            }
            catch { }
            return 0.0;
        }

        private double ReadTerminalPressureDrop(FamilyInstance term)
        {
            foreach (var name in PressureDropParameterNames)
            {
                var p = term.LookupParameter(name);
                if (p == null || !p.HasValue) continue;
                if (p.StorageType != StorageType.Double) continue;
                double raw = p.AsDouble();
                try
                {
                    var unitId = p.GetUnitTypeId();
                    if (unitId != null && !unitId.Empty())
                        return RevitUnits.PressureToSI(raw);
                }
                catch { }
                return raw;
            }
            return 0.0;
        }

        private static double ReadFlowValueSI(Parameter p)
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

        // -- Construccion de DuctSegment -------------------------------------

        private static DuctSegment BuildSegmentFromDuct(Duct duct)
        {
            double lenM = RevitUnits.GetLengthSI(duct, BuiltInParameter.CURVE_ELEM_LENGTH) ?? 0.0;
            double qM3s = RevitUnits.GetFlowSI(duct, BuiltInParameter.RBS_DUCT_FLOW_PARAM) ?? 0.0;
            string sysType = ReadSystemType(duct);

            DuctShape shape = DuctShape.Circular;
            double dM = 0.0, wM = 0.0, hM = 0.0;
            try
            {
                var conns = duct.ConnectorManager?.Connectors;
                if (conns != null)
                {
                    foreach (Connector c in conns)
                    {
                        if (c.ConnectorType != ConnectorType.End) continue;
                        if (c.Shape == ConnectorProfileType.Round)
                        {
                            shape = DuctShape.Circular;
                            dM = RevitUnits.LengthToSI(c.Radius * 2.0);
                        }
                        else if (c.Shape == ConnectorProfileType.Rectangular)
                        {
                            shape = DuctShape.Rectangular;
                            wM = RevitUnits.LengthToSI(c.Width);
                            hM = RevitUnits.LengthToSI(c.Height);
                        }
                        else if (c.Shape == ConnectorProfileType.Oval)
                        {
                            shape = DuctShape.Oval;
                            wM = RevitUnits.LengthToSI(c.Width);
                            hM = RevitUnits.LengthToSI(c.Height);
                        }
                        break;
                    }
                }
            }
            catch { }

            if (shape == DuctShape.Circular && dM <= 0.0)
                dM = RevitUnits.GetLengthSI(duct, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM) ?? 0.0;
            if (shape == DuctShape.Rectangular && (wM <= 0.0 || hM <= 0.0))
            {
                wM = RevitUnits.GetLengthSI(duct, BuiltInParameter.RBS_CURVE_WIDTH_PARAM)  ?? 0.0;
                hM = RevitUnits.GetLengthSI(duct, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM) ?? 0.0;
            }

            return new DuctSegment
            {
                RevitId    = duct.Id.Value,
                Shape      = shape,
                DiameterM  = dM,
                WidthM     = wM,
                HeightM    = hM,
                LengthM    = lenM,
                FlowM3s    = qM3s,
                Roughness  = 0.0001,
                SystemType = sysType,
                LevelName  = ReadLevelName(duct),
            };
        }

        /// <summary>Lee el nombre del Reference Level del duct.</summary>
        private static string ReadLevelName(Duct duct)
        {
            try
            {
                if (duct.ReferenceLevel != null && !string.IsNullOrEmpty(duct.ReferenceLevel.Name))
                    return duct.ReferenceLevel.Name;
                var p = duct.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (p != null && p.HasValue) return p.AsValueString() ?? p.AsString() ?? "";
            }
            catch { }
            return "";
        }

        private static string ReadSystemType(Duct duct)
        {
            try
            {
                var p = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                if (p != null && p.HasValue) return p.AsValueString() ?? p.AsString();
            }
            catch { }
            return null;
        }

        // -- Helpers ---------------------------------------------------------

        private static List<Duct> GetDuctsInSystem(Document doc, MechanicalSystem system)
        {
            var result = new List<Duct>();
            var network = system.DuctNetwork;
            if (network == null) return result;
            foreach (Element e in network)
            {
                if (e is Duct d) result.Add(d);
            }
            return result;
        }

        /// <summary>Devuelve los End connectors de cualquier elemento MEP soportado.</summary>
        private static List<Connector> GetEndConnectors(Element e)
        {
            var result = new List<Connector>();
            ConnectorManager cm = null;
            if (e is Duct d) cm = d.ConnectorManager;
            else if (e is FlexDuct fd) cm = fd.ConnectorManager;
            else if (e is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
            if (cm == null) return result;
            foreach (Connector c in cm.Connectors)
            {
                if (c.ConnectorType == ConnectorType.End) result.Add(c);
            }
            return result;
        }

        /// <summary>Clave estable y unica para identificar un Connector por (Owner.Id, Connector.Id).</summary>
        private static long ConnKey(Connector c)
        {
            if (c == null || c.Owner == null) return 0L;
            return c.Owner.Id.Value * 1_000_000L + c.Id;
        }

        /// <summary>Union-Find sencillo sobre claves long.</summary>
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
                if (p != x)
                {
                    p = Find(p);
                    _parent[x] = p;
                }
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
