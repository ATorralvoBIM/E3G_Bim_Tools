using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace E3G.Saneamiento.Hs5
{
    /// <summary>
    /// Adaptador Revit → motor HS5: monta la red conectada a la selección
    /// recorriendo conectores, la clasifica (Hs5Topologia), la evalúa
    /// (Hs5Evaluador) y devuelve los resultados de los elementos seleccionados.
    /// </summary>
    public static class Hs5Analisis
    {
        /// <summary>Tope de tuberías recorridas para no colgar Revit en modelos enormes.</summary>
        private const int MaxTuberias = 30000;

        public sealed class Resumen
        {
            public int OmitidasOrientacion { get; set; }
            public int Bajantes { get; set; }
            public int Desviaciones { get; set; }
            public int Derivaciones { get; set; }
            public int Ventilaciones { get; set; }
            public int Aparatos { get; set; }
            public int AparatosSinMapear { get; set; }
            public int Arquetas { get; set; }
            public bool RedTruncada { get; set; }
        }

        /// <summary>Tipo de aparato presente en el modelo, para la pestaña de mapeo.</summary>
        public sealed class TipoAparatoModelo
        {
            public string Clave { get; set; }
            public string Familia { get; set; }
            public string Tipo { get; set; }
            public int Cantidad { get; set; }
            public int DNDesague { get; set; }
            /// <summary>UD que lleva la familia (NaN si no tiene el parámetro).</summary>
            public double UD { get; set; } = double.NaN;
            public List<long> Ids { get; } = new List<long>();
        }

        /// <param name="progreso">Opcional: (porcentaje 0-100, fase) para la barra de estado.</param>
        public static List<ResultadoTuberia> Analizar(Document doc, ICollection<ElementId> seleccion,
                                                      Hs5Config cfg, out Resumen resumen,
                                                      Action<double, string> progreso = null)
        {
            resumen = new Resumen();
            progreso?.Invoke(2, "Leyendo la selección");
            var seleccionadas = seleccion
                .Select(doc.GetElement)
                .OfType<Pipe>()
                .Where(p => cfg.RedDe(SistemaDe(p)) != null)
                .ToList();
            if (seleccionadas.Count == 0) return new List<ResultadoTuberia>();

            Hs5Proyecto.Aplicar(doc, cfg);
            progreso?.Invoke(8, "Recorriendo la red conectada");
            var red = RecorrerRed(doc, seleccionadas, cfg);
            resumen.RedTruncada = red.Truncada;

            progreso?.Invoke(35, $"Leyendo {red.Tuberias.Count} tuberías");
            var tramos = new List<TramoHs5>(red.Tuberias.Count);
            for (int i = 0; i < red.Tuberias.Count; i++)
            {
                tramos.Add(CrearTramo(red.Tuberias[i], red.JuntaDe, cfg));
                if (progreso != null && (i % 50 == 0)) progreso(35 + 25.0 * i / red.Tuberias.Count, $"Leyendo {red.Tuberias.Count} tuberías");
            }
            progreso?.Invoke(60, "Resolviendo aparatos sanitarios");

            // Aparatos sanitarios por junta, ya resueltos con el mapeo y el uso.
            var aparatos = new Dictionary<string, List<AparatoConectado>>();
            foreach (var (fi, dn) in red.Aparatos)
            {
                var sym = fi.Symbol;
                string familia = sym?.FamilyName ?? fi.Name, tipo = sym?.Name ?? "";
                var a = Hs5Aparatos.Resolver(fi.Id.Value, familia, tipo,
                    Hs5Mapeo.De(Hs5Aparatos.Clave(familia, tipo)), cfg.UsoPublico, dn, UDAparato(fi, cfg));
                string j = red.JuntaDeElemento(fi);
                if (!aparatos.TryGetValue(j, out var l)) aparatos[j] = l = new List<AparatoConectado>();
                l.Add(a);
                if (a.SinMapear) resumen.AparatosSinMapear++;
            }
            resumen.Aparatos = red.Aparatos.Count;

            var arquetas = red.Arquetas.Select(e => LeerArqueta(e, red.JuntaDeElemento(e), cfg)).ToList();

            progreso?.Invoke(70, "Clasificando la red");
            var clasif = Hs5Topologia.Clasificar(tramos, NivelesZ(doc), aparatos.Keys.ToList());
            resumen.Bajantes = clasif.Bajantes.Count;
            resumen.Desviaciones = clasif.Desviaciones.Count;
            resumen.Derivaciones = clasif.Derivaciones.Count;
            resumen.Ventilaciones = clasif.Ventilaciones.Count;

            var ids = seleccionadas.Select(p => p.Id.Value).ToList();
            progreso?.Invoke(80, "Evaluando con las tablas CTE DB-HS5");
            var evals = Hs5Evaluador.Evaluar(tramos, clasif, cfg, ids, out int omitidas, aparatos, arquetas);
            resumen.OmitidasOrientacion = omitidas;
            resumen.Arquetas = evals.Count(e => e.Tipo == TipoTramo.Arqueta);

            var tramoPorId = tramos.ToDictionary(t => t.Id);
            var res = new List<ResultadoTuberia>();
            int n = 0;
            foreach (var ev in evals)
            {
                if (progreso != null && (n++ % 50 == 0)) progreso(88 + 7.0 * n / Math.Max(1, evals.Count), "Preparando resultados");
                var el = doc.GetElement(new ElementId(ev.Id));
                if (el == null) continue;
                tramoPorId.TryGetValue(ev.Id, out var t);
                res.Add(new ResultadoTuberia
                {
                    Id = el.Id,
                    SysType = SistemaDe(el),
                    Red = ev.Red,
                    UD = t?.UDCalculo ?? 0,
                    Superficie = t?.SuperficieCalculo ?? 0,
                    DNActual = ev.DNActual,
                    DNCorrect = ev.DNMinimo,
                    TextoExigido = ev.TextoExigido,
                    TablaId = ev.TablaId,
                    Columna = ev.Columna,
                    Tipo = ev.Tipo,
                    Criterio = ev.Criterio,
                    Avisos = string.Join("; ", ev.Avisos),
                    PendientePct = t == null || t.EsVertical ? double.NaN : t.PendienteCalculoPct,
                    PendienteInsuficiente = ev.PendienteInsuficiente,
                    FueraDeTabla = ev.FueraDeTabla,
                    EsCorrect = ev.EsCorrecto,
                    ColorSys = ColorSistema(doc, el),
                });
            }
            return res;
        }

        /// <summary>Tipos de aparato sanitario del modelo, agrupados por familia y tipo.</summary>
        public static List<TipoAparatoModelo> ListarTiposAparato(Document doc, Hs5Config cfg)
        {
            var tipos = new Dictionary<string, TipoAparatoModelo>();
            foreach (var fi in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                         .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                string familia = fi.Symbol?.FamilyName ?? fi.Name, tipo = fi.Symbol?.Name ?? "";
                string clave = Hs5Aparatos.Clave(familia, tipo);
                if (!tipos.TryGetValue(clave, out var t))
                    tipos[clave] = t = new TipoAparatoModelo { Clave = clave, Familia = familia, Tipo = tipo };
                t.Cantidad++;
                t.Ids.Add(fi.Id.Value);
                t.DNDesague = Math.Max(t.DNDesague, DiametroDesague(fi));
                if (double.IsNaN(t.UD)) t.UD = UDAparato(fi, cfg);
            }
            return tipos.Values.OrderBy(t => t.Familia).ThenBy(t => t.Tipo).ToList();
        }

        // ── Recorrido de la red ──

        private sealed class RedRevit
        {
            public List<Pipe> Tuberias;
            public List<(FamilyInstance Aparato, int DN)> Aparatos;
            public List<Element> Arquetas;
            public bool Truncada;
            public Func<Pipe, Connector, string> JuntaDe;
            public Func<Element, string> JuntaDeElemento;
        }

        /// <summary>
        /// BFS por conectores desde las tuberías seleccionadas. Los accesorios
        /// (codos, tes, reducciones, válvulas…) y los aparatos son juntas; elementos
        /// unidos entre sí se funden en una sola junta (union-find).
        /// </summary>
        private static RedRevit RecorrerRed(Document doc, List<Pipe> inicio, Hs5Config cfg)
        {
            var uf = new UnionFind();
            var tuberias = new Dictionary<long, Pipe>();
            var aparatos = new Dictionary<long, (FamilyInstance, int)>();
            var arquetas = new Dictionary<long, Element>();
            var visitadosNoTubo = new HashSet<long>();
            var cola = new Queue<Element>();
            foreach (var p in inicio) if (tuberias.TryAdd(p.Id.Value, p)) cola.Enqueue(p);
            bool truncada = false;

            while (cola.Count > 0)
            {
                var e = cola.Dequeue();
                foreach (Connector c in Conectores(e))
                {
                    if (c.ConnectorType == ConnectorType.Logical || !c.IsConnected) continue;
                    string nodo = ClaveNodo(e, c);
                    foreach (Connector r in c.AllRefs)
                    {
                        if (r.ConnectorType == ConnectorType.Logical) continue;
                        var otro = r.Owner;
                        if (otro == null || otro.Id == e.Id || otro is MEPSystem) continue;

                        uf.Unir(nodo, ClaveNodo(otro, r));
                        if (EsArqueta(otro)) arquetas.TryAdd(otro.Id.Value, otro);

                        if (otro is Pipe po)
                        {
                            if (cfg.RedDe(SistemaDe(po)) == null) continue;
                            if (tuberias.Count >= MaxTuberias) { truncada = true; continue; }
                            if (tuberias.TryAdd(po.Id.Value, po)) cola.Enqueue(po);
                        }
                        else if (EsPasante(otro) || EsArqueta(otro)) // por la arqueta sigue el colector
                        {
                            // Un sumidero modelado como accesorio también es origen de derivación.
                            if (otro is FamilyInstance acc && EsSumideroAccesorio(acc))
                                aparatos.TryAdd(acc.Id.Value, (acc, r.Shape == ConnectorProfileType.Round ? Mm(r.Radius * 2) : 0));
                            if (visitadosNoTubo.Add(otro.Id.Value)) cola.Enqueue(otro);
                        }
                        else if (otro is FamilyInstance fi && EsAparato(fi))
                        {
                            int dn = r.Shape == ConnectorProfileType.Round ? Mm(r.Radius * 2) : 0;
                            if (aparatos.TryGetValue(fi.Id.Value, out var prev)) dn = Math.Max(dn, prev.Item2);
                            aparatos[fi.Id.Value] = (fi, dn);
                        }
                    }
                }
            }
            return new RedRevit
            {
                Tuberias = tuberias.Values.ToList(),
                Aparatos = aparatos.Values.ToList(),
                Arquetas = arquetas.Values.ToList(),
                Truncada = truncada,
                JuntaDe = (p, c) => uf.Raiz(ClaveNodo(p, c)),
                JuntaDeElemento = el => uf.Raiz($"F{el.Id.Value}"),
            };
        }

        /// <summary>Accesorios de tubería: el agua los atraviesa. Aparatos y equipos son finales de red.</summary>
        private static bool EsPasante(Element e)
        {
            long cat = e.Category?.Id.Value ?? 0;
            return cat == (long)BuiltInCategory.OST_PipeFitting || cat == (long)BuiltInCategory.OST_PipeAccessory;
        }

        private static bool EsAparato(FamilyInstance fi) =>
            fi.Category?.Id.Value == (long)BuiltInCategory.OST_PlumbingFixtures && !EsArqueta(fi);

        private static bool EsSumideroAccesorio(FamilyInstance fi) =>
            fi.Category?.Id.Value == (long)BuiltInCategory.OST_PipeAccessory &&
            Hs5Aparatos.Detectar(fi.Symbol?.FamilyName ?? fi.Name, fi.Symbol?.Name ?? "") == "Sumidero sifónico";

        private static bool EsArqueta(Element e)
        {
            string n = $"{(e as FamilyInstance)?.Symbol?.FamilyName} {e.Name}";
            return n.IndexOf("arqueta", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Un nodo por extremo de tubería; un único nodo por accesorio, aparato o arqueta.</summary>
        private static string ClaveNodo(Element e, Connector c) =>
            e is Pipe ? $"P{e.Id.Value}:{c.Id}" : $"F{e.Id.Value}";

        private static IEnumerable<Connector> Conectores(Element e)
        {
            ConnectorSet set = (e as MEPCurve)?.ConnectorManager?.Connectors
                            ?? (e as FamilyInstance)?.MEPModel?.ConnectorManager?.Connectors;
            return set == null ? Enumerable.Empty<Connector>() : set.Cast<Connector>();
        }

        private static TramoHs5 CrearTramo(Pipe p, Func<Pipe, Connector, string> juntaDe, Hs5Config cfg)
        {
            var extremos = Conectores(p).Where(c => c.ConnectorType == ConnectorType.End).ToList();
            XYZ a, b;
            string j0 = null, j1 = null;
            if (extremos.Count >= 2)
            {
                a = extremos[0].Origin; b = extremos[1].Origin;
                if (extremos[0].IsConnected) j0 = juntaDe(p, extremos[0]);
                if (extremos[1].IsConnected) j1 = juntaDe(p, extremos[1]);
            }
            else
            {
                var lc = (LocationCurve)p.Location;
                a = lc.Curve.GetEndPoint(0); b = lc.Curve.GetEndPoint(1);
            }
            var red = cfg.RedDe(SistemaDe(p)) ?? RedHs5Tipo.Residual;
            double ud = LeerNumero(p.LookupParameter(cfg.ParametroUD));
            double superficie = LeerSuperficie(p.LookupParameter(cfg.ParametroSuperficie));
            // En pluviales el parámetro de UD lleva la superficie de recogida (m²) de los sumideros.
            if (red == RedHs5Tipo.Pluvial && cfg.SuperficiePluvialEnParametroUD) { superficie = ud; ud = 0; }
            return new TramoHs5
            {
                Id = p.Id.Value,
                Red = red,
                UD = ud,
                Superficie = superficie,
                DNActual = LeerDN(p),
                Junta0 = j0,
                Junta1 = j1,
                Z0 = Metros(a.Z),
                Z1 = Metros(b.Z),
                LongHorizontal = Metros(Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2))),
            };
        }

        private static ArquetaHs5 LeerArqueta(Element e, string junta, Hs5Config cfg)
        {
            Parameter Buscar(string nombre) =>
                e.LookupParameter(nombre) ?? (e as FamilyInstance)?.Symbol?.LookupParameter(nombre);
            return new ArquetaHs5
            {
                Id = e.Id.Value,
                Junta = junta,
                LargoCm = LeerCm(Buscar(cfg.ParametroLargoArqueta)),
                AnchoCm = LeerCm(Buscar(cfg.ParametroAnchoArqueta)),
            };
        }

        /// <summary>Cotas de los niveles que son planta del edificio, en m (coordenadas internas).</summary>
        private static List<double> NivelesZ(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Where(l => l.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY)?.AsInteger() == 1)
                .Select(l => Metros(l.ProjectElevation))
                .OrderBy(z => z)
                .ToList();

        // ── Lecturas ──

        private static double Metros(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);
        private static int Mm(double pies) => (int)Math.Round(UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Millimeters));

        private static double LeerNumero(Parameter p)
        {
            if (p == null) return 0;
            switch (p.StorageType)
            {
                case StorageType.Double: return p.AsDouble();
                case StorageType.Integer: return p.AsInteger();
                case StorageType.String:
                    return double.TryParse((p.AsString() ?? "").Replace(',', '.'), NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out double v) ? v : 0;
                default: return 0;
            }
        }

        /// <summary>m²: si el parámetro es de tipo Área se convierte desde unidades internas.</summary>
        private static double LeerSuperficie(Parameter p)
        {
            if (p == null) return 0;
            if (p.StorageType == StorageType.Double && p.Definition?.GetDataType() == SpecTypeId.Area)
                return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.SquareMeters);
            return LeerNumero(p);
        }

        /// <summary>cm: si el parámetro es de tipo Longitud se convierte; si es número, se toma en cm.</summary>
        private static double LeerCm(Parameter p)
        {
            if (p == null || !p.HasValue) return double.NaN;
            if (p.StorageType == StorageType.Double && p.Definition?.GetDataType() == SpecTypeId.Length)
                return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Centimeters);
            double v = LeerNumero(p);
            return v > 0 ? v : double.NaN;
        }

        private static string SistemaDe(Element e) =>
            e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString() ?? "";

        private static string ColorSistema(Document doc, Element e)
        {
            var sysTypeId = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
            var sysTypeEl = sysTypeId != null ? doc.GetElement(sysTypeId) : null;
            return sysTypeEl?.LookupParameter("Color de Sistema")?.AsValueString() ?? "127-127-127";
        }

        private static int LeerDN(Element e)
        {
            int dn = CteMotor.ParseDN(e.LookupParameter("Tamaño")?.AsValueString());
            if (dn > 0) return dn;
            var p = e.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            return p == null ? 0 : Mm(p.AsDouble());
        }

        /// <summary>UD de la familia del aparato (instancia o tipo); NaN si no tiene el parámetro.</summary>
        private static double UDAparato(FamilyInstance fi, Hs5Config cfg)
        {
            foreach (string nombre in new[] { cfg.ParametroUDAparato, cfg.ParametroUD })
            {
                var p = fi.LookupParameter(nombre) ?? fi.Symbol?.LookupParameter(nombre);
                if (p != null && p.HasValue) return LeerNumero(p);
            }
            return double.NaN;
        }

        /// <summary>Ø del desagüe del aparato: mayor conector redondo de saneamiento.</summary>
        private static int DiametroDesague(FamilyInstance fi)
        {
            int dn = 0;
            foreach (var c in Conectores(fi))
            {
                if (c.Domain != Domain.DomainPiping || c.Shape != ConnectorProfileType.Round) continue;
                if (c.PipeSystemType != PipeSystemType.Sanitary) continue;
                dn = Math.Max(dn, Mm(c.Radius * 2));
            }
            return dn;
        }

        /// <summary>Cambia el DN de las tuberías indicadas; devuelve cuántas no se pudieron cambiar.</summary>
        public static int AplicarDN(Document doc, IEnumerable<(ElementId Id, int DN)> cambios, out int aplicadas)
        {
            aplicadas = 0;
            int fallos = 0;
            foreach (var (id, dn) in cambios)
            {
                var p = doc.GetElement(id) as Pipe;
                var par = p?.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (par == null || par.IsReadOnly || dn <= 0) { fallos++; continue; }
                try
                {
                    par.Set(UnitUtils.ConvertToInternalUnits(dn, UnitTypeId.Millimeters));
                    aplicadas++;
                }
                catch { fallos++; }
            }
            return fallos;
        }

        private sealed class UnionFind
        {
            private readonly Dictionary<string, string> _padre = new Dictionary<string, string>();

            // Iterativo: en redes largas la recursión podría desbordar la pila.
            public string Raiz(string x)
            {
                if (!_padre.ContainsKey(x)) { _padre[x] = x; return x; }
                string r = x;
                while (_padre[r] != r) r = _padre[r];
                while (_padre[x] != r) { string sig = _padre[x]; _padre[x] = r; x = sig; }
                return r;
            }

            public void Unir(string a, string b)
            {
                string ra = Raiz(a), rb = Raiz(b);
                if (ra != rb) _padre[ra] = rb;
            }
        }
    }
}
