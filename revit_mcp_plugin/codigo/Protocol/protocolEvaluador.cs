// revit_mcp_plugin/codigo/Protocol/ProtocolEvaluador.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.Protocol
{
    /// <summary>
    /// Avalua les regles del Protocol contra elements reals del model de Revit.
    /// </summary>
    public sealed class ProtocolEvaluador
    {
        private readonly ProtocolInstalacio _protocol;
        private readonly Document _doc;

        // Caché d'elevació per nom de nivell (peus)
        private Dictionary<string, double>? _elevacioCache;

        public ProtocolEvaluador(ProtocolInstalacio protocol, Document doc)
        {
            _protocol = protocol;
            _doc = doc;
        }

        // ── API pública ──────────────────────────────────────────────────────

        public string? GrupDePipe(Element pipe)
        {
            // 1. Interior vivendes — pipe dins d'un Group de Revit
            if (pipe.GroupId != ElementId.InvalidElementId)
            {
                var grup = _doc.GetElement(pipe.GroupId) as Group;
                if (grup != null)
                {
                    var tipusGrup = _doc.GetElement(grup.GetTypeId()) as GroupType;
                    if (tipusGrup != null && MatchSeccio(_protocol.Vivendes, tipusGrup))
                        return "Interior vivendes";
                }
            }

            // 2. Aparcament
            var nivellNom = ObtenirNivellNom(pipe);
            if (MatchSeccioNivell(_protocol.Aparcament, pipe, nivellNom))
                return "Aparcament";

            // 3. Bajants / Col·lectors — criteri geomètric
            if (pipe.Location is LocationCurve lc)
            {
                var p0 = lc.Curve.GetEndPoint(0);
                var p1 = lc.Curve.GetEndPoint(1);
                var dz = Math.Abs(p1.Z - p0.Z);
                var dxy = Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
                var longM = lc.Curve.Length * 0.3048;

                bool esVertical = dz > dxy && longM >= _protocol.BajantLongitudMinM;
                bool esHoritzontal = dxy >= dz;

                if (esVertical && MatchSeccioGeom(_protocol.Bajants, pipe, nivellNom, esVertical, esHoritzontal, longM, p0))
                    return "Bajants";
                if (esHoritzontal && MatchSeccioGeom(_protocol.Colectors, pipe, nivellNom, esVertical, esHoritzontal, longM, p0))
                    return "Col·lectors P00-P01";
            }

            // 4. Edifici (retorna nom de l'edifici si coincideix)
            foreach (var regla in _protocol.Edificis.Regles)
                if (AvaliarRegla(regla, pipe))
                    return regla.Nom;

            return null; // Fallback: Sistema+Nivell
        }

        public string? GrupDeGroupType(GroupType gt)
        {
            if (MatchSeccio(_protocol.Vivendes, gt)) return "Interior vivendes";
            return null;
        }

        /// <summary>Llista elements d'un tipus que coincideixen amb una secció.</summary>
        public IEnumerable<Element> ElementsQueCoincideixen(ProtocolSeccio seccio, IEnumerable<Element> elements)
            => elements.Where(e => MatchSeccio(seccio, e));

        // ── Match de secció ──────────────────────────────────────────────────

        private bool MatchSeccio(ProtocolSeccio seccio, Element element)
            => seccio.Regles.Any(r => AvaliarRegla(r, element));

        private bool MatchSeccioNivell(ProtocolSeccio seccio, Element element, string? nivellNom)
        {
            // Les regles d'aparcament s'apliquen al Nivell de l'element
            if (nivellNom == null) return false;
            var nivells = ObtenirNivells();
            var nivell = nivells.FirstOrDefault(n => n.Name == nivellNom);
            if (nivell == null) return false;
            return seccio.Regles.Any(r => AvaliarRegla(r, nivell));
        }

        private bool MatchSeccioGeom(ProtocolSeccio seccio, Element pipe, string? nivellNom,
                                     bool esVert, bool esHorit, double longM, XYZ p0)
        {
            if (!seccio.Regles.Any()) return false;
            return seccio.Regles.Any(r => AvaliarReglaGeom(r, pipe, nivellNom, esVert, esHorit, longM, p0));
        }

        // ── Avaluació d'una regla ────────────────────────────────────────────

        private bool AvaliarRegla(ProtocolRegla regla, Element element)
        {
            if (!regla.Condicions.Any()) return false;
            return regla.Logica == LogicaCondicions.Totes
                ? regla.Condicions.All(c => AvaliarCondicio(c, element))
                : regla.Condicions.Any(c => AvaliarCondicio(c, element));
        }

        private bool AvaliarReglaGeom(ProtocolRegla regla, Element pipe, string? nivellNom,
                                      bool esVert, bool esHorit, double longM, XYZ p0)
        {
            if (!regla.Condicions.Any()) return false;
            return regla.Logica == LogicaCondicions.Totes
                ? regla.Condicions.All(c => AvaliarCondicioGeom(c, pipe, nivellNom, esVert, esHorit, longM, p0))
                : regla.Condicions.Any(c => AvaliarCondicioGeom(c, pipe, nivellNom, esVert, esHorit, longM, p0));
        }

        // ── Avaluació d'una condició ─────────────────────────────────────────

        private bool AvaliarCondicio(FilterCondicio condicio, Element element)
        {
            // Paràmetres virtuals geomètrics no s'apliquen aquí
            if (condicio.ParamNom.StartsWith("__GEOM_")) return false;

            var valorElement = ObtenirValorParam(element, condicio.ParamNom);
            if (condicio.Operador.EsNumeric())
            {
                if (!double.TryParse(valorElement, out var numElem)) return false;
                if (!double.TryParse(condicio.Valor, out var numRegla)) return false;
                return condicio.Operador.AvalurNumeric(numElem, numRegla);
            }
            return condicio.Operador.Avaluar(valorElement, condicio.Valor);
        }

        private bool AvaliarCondicioGeom(FilterCondicio condicio, Element pipe, string? nivellNom,
                                         bool esVert, bool esHorit, double longM, XYZ p0)
        {
            // Paràmetres virtuals geomètrics
            switch (condicio.ParamNom)
            {
                case ParamVirtual.Vertical:
                    return condicio.Operador.Avaluar(esVert ? "true" : "false", "true");
                case ParamVirtual.Horitzontal:
                    return condicio.Operador.Avaluar(esHorit ? "true" : "false", "true");
                case ParamVirtual.LongitudM:
                    if (!double.TryParse(condicio.Valor, out var minL)) return false;
                    return condicio.Operador.AvalurNumeric(longM, minL);
                case ParamVirtual.CentreZ_M:
                    var centreZ = (p0.Z + p0.Z) / 2.0 * 0.3048;
                    if (!double.TryParse(condicio.Valor, out var zVal)) return false;
                    return condicio.Operador.AvalurNumeric(centreZ, zVal);
                case ParamVirtual.ElevacioNivell_M:
                    if (nivellNom == null) return false;
                    if (!_elevacioCache.TryGetValue(nivellNom, out var elev)) return false;
                    var elevM = elev * 0.3048;
                    if (!double.TryParse(condicio.Valor, out var elevVal)) return false;
                    return condicio.Operador.AvalurNumeric(elevM, elevVal);
                default:
                    return AvaliarCondicio(condicio, pipe);
            }
        }

        // ── Helpers de Revit ─────────────────────────────────────────────────

        private string? ObtenirValorParam(Element element, string nomParam)
        {
            // Workset
            if (nomParam == "Workset")
            {
                var ws = _doc.GetWorksetTable().GetWorkset(element.WorksetId);
                return ws?.Name;
            }

            // Paràmetre per nom
            var param = element.LookupParameter(nomParam);
            if (param == null)
            {
                // Intenta per BuiltInParameter
                foreach (Parameter p in element.Parameters)
                    if (p.Definition?.Name == nomParam)
                    { param = p; break; }
            }
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

        private List<Level> ObtenirNivells()
        {
            if (_elevacioCache == null)
            {
                _elevacioCache = new Dictionary<string, double>();
                var nivells = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .ToList();
                foreach (var n in nivells)
                    _elevacioCache[n.Name] = n.Elevation;
                return nivells;
            }
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).Cast<Level>().ToList();
        }
    }
}