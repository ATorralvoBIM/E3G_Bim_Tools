using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.Protocol
{
    public sealed class ProtocolEvaluador
    {
        private readonly ProtocolInstalacio _protocol;
        private readonly Document _doc;
        private Dictionary<string, double> _elevacioCache = new();

        public ProtocolEvaluador(ProtocolInstalacio protocol, Document doc)
        {
            _protocol = protocol;
            _doc = doc;
        }

        /// <summary>
        /// Retorna l'etiqueta de la primera secció que coincideix amb el pipe,
        /// o null si cap secció el classifica.
        /// </summary>
        public string? GrupDePipe(Element pipe)
        {
            // 1. Primer: pipes dins d'un Group de Revit → busquem secció per GroupType
            if (pipe.GroupId != ElementId.InvalidElementId)
            {
                var grup = _doc.GetElement(pipe.GroupId) as Group;
                var gt = grup != null ? _doc.GetElement(grup.GetTypeId()) as GroupType : null;
                if (gt != null)
                {
                    foreach (var seccio in _protocol.Seccions)
                        if (seccio.Regles.Any(r => r.Aplica == ElementTipus.GrupTipus
                                                && AvaliarRegla(r, gt)))
                            return seccio.Etiqueta;
                }
            }

            // 2. Calcular dades geomètriques una sola vegada
            var nivellNom = ObtenirNivellNom(pipe);
            bool esVert = false, esHorit = false;
            double longM = 0, centreZ = 0;

            if (pipe.Location is LocationCurve lc)
            {
                var p0 = lc.Curve.GetEndPoint(0);
                var p1 = lc.Curve.GetEndPoint(1);
                var dz = Math.Abs(p1.Z - p0.Z);
                var dxy = Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
                longM = lc.Curve.Length * 0.3048;
                centreZ = ((p0.Z + p1.Z) / 2.0) * 0.3048;
                esVert = dz > dxy && longM >= _protocol.BajantLongitudMinM;
                esHorit = dxy >= dz;
            }

            // 3. Iterar totes les seccions en ordre
            foreach (var seccio in _protocol.Seccions)
            {
                foreach (var regla in seccio.Regles)
                {
                    bool match = regla.Aplica switch
                    {
                        ElementTipus.Canonada => AvaliarReglaGeom(regla, pipe, nivellNom,
                                                      esVert, esHorit, longM, centreZ),
                        ElementTipus.Nivell => AvaliarReglaNivell(regla, nivellNom),
                        ElementTipus.GrupTipus => false, // ja tractat a dalt
                        ElementTipus.Workset => AvaliarRegla(regla, pipe),
                        _ => false
                    };
                    if (match) return seccio.Etiqueta;
                }
            }

            return null;
        }

        // ── Avaluació de regla sobre element ────────────────────────────────

        private bool AvaliarRegla(ProtocolRegla regla, Element element)
        {
            if (!regla.Condicions.Any()) return false;
            return regla.Logica == LogicaCondicions.Totes
                ? regla.Condicions.All(c => AvaliarCondicio(c, element))
                : regla.Condicions.Any(c => AvaliarCondicio(c, element));
        }

        private bool AvaliarReglaGeom(ProtocolRegla regla, Element pipe,
            string? nivellNom, bool esVert, bool esHorit, double longM, double centreZ)
        {
            if (!regla.Condicions.Any()) return false;
            return regla.Logica == LogicaCondicions.Totes
                ? regla.Condicions.All(c => AvaliarCondicioGeom(c, pipe, nivellNom,
                                              esVert, esHorit, longM, centreZ))
                : regla.Condicions.Any(c => AvaliarCondicioGeom(c, pipe, nivellNom,
                                              esVert, esHorit, longM, centreZ));
        }

        private bool AvaliarReglaNivell(ProtocolRegla regla, string? nivellNom)
        {
            if (nivellNom == null || !regla.Condicions.Any()) return false;
            var nivells = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).Cast<Level>().ToList();
            var nivell = nivells.FirstOrDefault(n => n.Name == nivellNom);
            if (nivell == null) return false;
            return regla.Logica == LogicaCondicions.Totes
                ? regla.Condicions.All(c => AvaliarCondicio(c, nivell))
                : regla.Condicions.Any(c => AvaliarCondicio(c, nivell));
        }

        private bool AvaliarCondicio(FilterCondicio c, Element element)
        {
            if (c.ParamNom.StartsWith("__GEOM_")) return false;
            var val = ObtenirValorParam(element, c.ParamNom);
            if (c.Operador.EsNumeric())
            {
                if (!double.TryParse(val, out var numE)) return false;
                if (!double.TryParse(c.Valor, out var numR)) return false;
                return c.Operador.AvalurNumeric(numE, numR);
            }
            return c.Operador.Avaluar(val ?? "", c.Valor);
        }

        private bool AvaliarCondicioGeom(FilterCondicio c, Element pipe,
            string? nivellNom, bool esVert, bool esHorit, double longM, double centreZ)
        {
            switch (c.ParamNom)
            {
                case ParamVirtual.Vertical:
                    return c.Operador.Avaluar(esVert ? "true" : "false", "true");
                case ParamVirtual.Horitzontal:
                    return c.Operador.Avaluar(esHorit ? "true" : "false", "true");
                case ParamVirtual.LongitudM:
                    if (!double.TryParse(c.Valor, out var minL)) return false;
                    return c.Operador.AvalurNumeric(longM, minL);
                case ParamVirtual.CentreZ_M:
                    if (!double.TryParse(c.Valor, out var zVal)) return false;
                    return c.Operador.AvalurNumeric(centreZ, zVal);
                case ParamVirtual.ElevacioNivell_M:
                    if (nivellNom == null) return false;
                    CarregarElevacionsCache();
                    if (!_elevacioCache.TryGetValue(nivellNom, out var elev)) return false;
                    var elevM = elev * 0.3048;
                    if (!double.TryParse(c.Valor, out var elevVal)) return false;
                    return c.Operador.AvalurNumeric(elevM, elevVal);
                default:
                    return AvaliarCondicio(c, pipe);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private string? ObtenirValorParam(Element element, string nomParam)
        {
            if (nomParam == "Workset")
            {
                var ws = _doc.GetWorksetTable().GetWorkset(element.WorksetId);
                return ws?.Name;
            }
            var param = element.LookupParameter(nomParam);
            if (param == null)
                foreach (Parameter p in element.Parameters)
                    if (p.Definition?.Name == nomParam) { param = p; break; }
            if (param == null) return null;
            return param.StorageType switch
            {
                StorageType.String => param.AsString(),
                StorageType.Integer => param.AsInteger().ToString(),
                StorageType.Double => param.AsDouble().ToString("G"),
                StorageType.ElementId => _doc.GetElement(param.AsElementId())?.Name,
                _ => null
            };
        }

        private string? ObtenirNivellNom(Element element)
        {
            var p = element.LookupParameter("Reference Level")
                 ?? element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)
                 ?? element.get_Parameter(BuiltInParameter.LEVEL_PARAM);
            return p?.AsValueString();
        }

        private void CarregarElevacionsCache()
        {
            if (_elevacioCache.Count > 0) return;
            var nivells = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).Cast<Level>();
            foreach (var n in nivells)
                _elevacioCache[n.Name] = n.Elevation;
        }
    }
}