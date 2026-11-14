using System;
using System.Collections.Generic;
using System.Linq;

// =============================================================================
// CTE DB-HS5 — CLASIFICACIÓN TOPOLÓGICA DE TRAMOS
// -----------------------------------------------------------------------------
// Sin dependencia de Revit. La red se describe como tramos (tuberías) cuyos
// extremos caen en "juntas" (accesorio, unión directa o aparato). A partir de
// la geometría y de las UD acumuladas se decide el sentido del flujo y el tipo
// de cada tramo de desagüe:
//   · Derivación  — desde un aparato sanitario hasta la primera unión con otro
//                   ramal, colector o bajante (tabla 4.1: Ø del aparato).
//   · Bajante     — vertical (> 45°) que forma una columna de al menos
//                   LongitudMinimaBajante de recorrido vertical.
//   · Desviación  — tramo(s) horizontales sin ramificar entre dos bajantes
//                   (apdo. 4.1.2.3 b).
//   · Colector    — horizontal con alguna bajante aguas arriba.
//   · Ramal       — horizontal (o vertical corto) con sólo aparatos aguas arriba.
// y de ventilación (no transporta agua, se clasifica aparte):
//   · Primaria    — prolongación de la bajante por encima de su cabeza (4.4.1).
//   · Secundaria  — columna conectada a la bajante (4.4.2).
//   · Terciaria   — ramal de ventilación de los cierres hidráulicos (4.4.3).
// =============================================================================
namespace E3G.Saneamiento.Hs5
{
    public enum TipoTramo
    {
        Ramal, Bajante, Desviacion, Colector, Derivacion,
        VentPrimaria, VentSecundaria, VentTerciaria, Arqueta,
    }

    /// <summary>Tubería de la red. Cotas y longitudes en metros, DN en mm.</summary>
    public sealed class TramoHs5
    {
        public long Id { get; set; }
        public RedHs5Tipo Red { get; set; } = RedHs5Tipo.Residual;
        public double UD { get; set; }
        /// <summary>Superficie servida acumulada (m², redes pluviales y mixtas).</summary>
        public double Superficie { get; set; }
        public int DNActual { get; set; }

        private double? _udCalculo, _superficieCalculo;
        /// <summary>
        /// UD con la que se dimensiona: la del modelo o, si es menor, la suma de lo que le
        /// llega aguas arriba (parámetro sin recalcular tras modificar la red). La fija Clasificar.
        /// </summary>
        public double UDCalculo { get => _udCalculo ?? UD; set => _udCalculo = value; }
        /// <summary>Ídem para la superficie (m²).</summary>
        public double SuperficieCalculo { get => _superficieCalculo ?? Superficie; set => _superficieCalculo = value; }

        private double? _pendienteCalculo;
        /// <summary>
        /// Pendiente (%) con la que se elige la columna de la tabla: la propia o, en tramos
        /// cortos, la del tramo largo contiguo (ver Hs5Topologia.LongitudMinimaPendiente).
        /// </summary>
        public double PendienteCalculoPct { get => _pendienteCalculo ?? PendientePct; set => _pendienteCalculo = value; }

        /// <summary>Junta de cada extremo (null = extremo libre).</summary>
        public string Junta0 { get; set; }
        public string Junta1 { get; set; }
        public double Z0 { get; set; }
        public double Z1 { get; set; }
        public double LongHorizontal { get; set; }

        public double Dz => Math.Abs(Z1 - Z0);
        public double Longitud => Math.Sqrt(Dz * Dz + LongHorizontal * LongHorizontal);
        public double PendientePct => LongHorizontal < 1e-6 ? double.PositiveInfinity : Dz / LongHorizontal * 100.0;
        /// <summary>Más de 45° respecto a la horizontal, el umbral de desviaciones del HS5 (4.1.2.3).</summary>
        public bool EsVertical => Math.Atan2(Dz, LongHorizontal) > Math.PI / 4;
        public bool EsVentilacion => Red == RedHs5Tipo.Ventilacion;
        public double ZMin => Math.Min(Z0, Z1);
        public double ZMax => Math.Max(Z0, Z1);
    }

    public sealed class ClasificacionTramo
    {
        public TipoTramo Tipo { get; set; }
        /// <summary>Índice en RedHs5.Bajantes, o -1.</summary>
        public int Bajante { get; set; } = -1;
        /// <summary>Índice en RedHs5.Desviaciones, o -1.</summary>
        public int Desviacion { get; set; } = -1;
        /// <summary>Índice en RedHs5.Derivaciones, o -1.</summary>
        public int Derivacion { get; set; } = -1;
        /// <summary>Índice en RedHs5.Ventilaciones, o -1.</summary>
        public int Ventilacion { get; set; } = -1;
        /// <summary>Vertical demasiado corto para ser bajante (bajada de aparato): se trata como ramal.</summary>
        public bool EsCaidaCorta { get; set; }
        /// <summary>Con desnivel, el tramo sube en el sentido del agua (error de modelado).</summary>
        public bool Contrapendiente { get; set; }
        /// <summary>El parámetro de UD o superficie es menor que la suma de lo que le llega aguas arriba.</summary>
        public bool CargaDesactualizada { get; set; }
        /// <summary>Tramo corto que toma la pendiente de otro (su id), o -1.</summary>
        public long PendienteDe { get; set; } = -1;
        public string JuntaArriba { get; set; }
        public string JuntaAbajo { get; set; }
    }

    public sealed class BajanteHs5
    {
        public int Indice { get; set; }
        public RedHs5Tipo Red { get; set; }
        public List<long> Tramos { get; } = new List<long>();
        public double ZMin { get; set; }
        public double ZMax { get; set; }
        public double UDMax { get; set; }
        public double SuperficieMax { get; set; }
        public int DNActualMax { get; set; }
        /// <summary>Máximo de UD de los ramales que acometen a la bajante (0 si ninguno).</summary>
        public double UDRamalMax { get; set; }
        /// <summary>Plantas que vierten a la bajante (0 = desconocido).</summary>
        public int Plantas { get; set; }
        /// <summary>Desviación por la que llega desde una bajante superior, o -1.</summary>
        public int DesviacionEntrada { get; set; } = -1;
        internal HashSet<string> Juntas { get; } = new HashSet<string>();
        internal HashSet<int> NivelesVertido { get; } = new HashSet<int>();
    }

    public sealed class DesviacionHs5
    {
        public int Indice { get; set; }
        public List<long> Tramos { get; } = new List<long>();
        public int BajanteArriba { get; set; }
        public int BajanteAbajo { get; set; }
    }

    /// <summary>Derivación individual: del aparato a la primera unión.</summary>
    public sealed class DerivacionHs5
    {
        public int Indice { get; set; }
        public string JuntaAparato { get; set; }
        public List<long> Tramos { get; } = new List<long>();
        public double Longitud { get; set; }
    }

    public sealed class VentilacionHs5
    {
        public int Indice { get; set; }
        public TipoTramo Tipo { get; set; }
        public List<long> Tramos { get; } = new List<long>();
        public double Longitud { get; set; }
        /// <summary>Bajante a la que sirve (primaria/secundaria), o -1.</summary>
        public int Bajante { get; set; } = -1;
        /// <summary>Nº de juntas distintas en que se une a la bajante.</summary>
        public int UnionesBajante { get; set; }
        /// <summary>Tramo de desagüe al que ventila (terciaria), o -1.</summary>
        public long RamalVentilado { get; set; } = -1;
    }

    public sealed class RedHs5
    {
        public Dictionary<long, ClasificacionTramo> Tramos { get; } = new Dictionary<long, ClasificacionTramo>();
        public List<BajanteHs5> Bajantes { get; } = new List<BajanteHs5>();
        public List<DesviacionHs5> Desviaciones { get; } = new List<DesviacionHs5>();
        public List<DerivacionHs5> Derivaciones { get; } = new List<DerivacionHs5>();
        public List<VentilacionHs5> Ventilaciones { get; } = new List<VentilacionHs5>();
        /// <summary>Tramos de desagüe que entran en cada junta (para recorrer aguas arriba).</summary>
        internal Dictionary<string, List<TramoHs5>> EntradasPorJunta { get; } = new Dictionary<string, List<TramoHs5>>();

        public IReadOnlyList<TramoHs5> Entradas(string junta) =>
            junta != null && EntradasPorJunta.TryGetValue(junta, out var l) ? l : (IReadOnlyList<TramoHs5>)Array.Empty<TramoHs5>();
    }

    public static class Hs5Topologia
    {
        /// <summary>Recorrido vertical mínimo de una columna para considerarla bajante (m).</summary>
        public const double LongitudMinimaBajante = 1.5;
        /// <summary>
        /// Por debajo de esta longitud horizontal (m), la pendiente de un tramo (manguitos entre
        /// accesorios) es ruido de modelado: se toma la del tramo largo contiguo.
        /// </summary>
        public const double LongitudMinimaPendiente = 0.5;
        /// <summary>Distancia a la cabeza de la bajante para considerar primaria una ventilación (m).</summary>
        private const double TolCabezaBajante = 0.5;
        private const double TolZ = 0.001;

        /// <param name="juntasConAparato">Juntas donde hay un aparato sanitario (origen de derivaciones).</param>
        public static RedHs5 Clasificar(IList<TramoHs5> todos, IList<double> nivelesZ,
                                        ICollection<string> juntasConAparato = null,
                                        double longitudMinimaBajante = LongitudMinimaBajante)
        {
            var red = new RedHs5();
            var porId = todos.ToDictionary(t => t.Id);
            var tramos = todos.Where(t => !t.EsVentilacion).ToList();
            var aparatos = juntasConAparato ?? (ICollection<string>)Array.Empty<string>();

            // Tramos de desagüe por junta
            var enJunta = IndexarPorJunta(tramos);
            List<TramoHs5> Vecinos(string j, TramoHs5 excepto) =>
                j != null && enJunta.TryGetValue(j, out var l) ? l.Where(x => x != excepto).ToList() : new List<TramoHs5>();

            // ── 1. Sentido del flujo: hacia la salida de la red (su extremo libre más bajo) ──
            var abajoDe = OrientarRed(tramos, enJunta, aparatos, out var ordenDesdeSalida);
            foreach (var t in tramos)
            {
                if (!abajoDe.TryGetValue(t.Id, out int abajo))
                    abajo = ExtremoAguasAbajo(t, Vecinos(t.Junta0, t), Vecinos(t.Junta1, t));
                int masBajo = t.Z0 < t.Z1 ? 0 : 1;
                red.Tramos[t.Id] = new ClasificacionTramo
                {
                    Tipo = TipoTramo.Ramal,
                    JuntaAbajo = abajo == 0 ? t.Junta0 : t.Junta1,
                    JuntaArriba = abajo == 0 ? t.Junta1 : t.Junta0,
                    Contrapendiente = t.Dz > TolZ && masBajo != abajo,
                };
            }
            ClasificacionTramo C(TramoHs5 t) => red.Tramos[t.Id];
            foreach (var t in tramos)
            {
                string j = C(t).JuntaAbajo;
                if (j == null) continue;
                if (!red.EntradasPorJunta.TryGetValue(j, out var l)) red.EntradasPorJunta[j] = l = new List<TramoHs5>();
                l.Add(t);
            }
            List<TramoHs5> Entradas(string j) => red.Entradas(j).ToList();

            // Carga de cálculo: la del parámetro, o la suma de lo que entra si es mayor.
            var orden = tramos.Where(t => !abajoDe.ContainsKey(t.Id)).Concat(Enumerable.Reverse(ordenDesdeSalida));
            foreach (var t in orden)
            {
                double ud = 0, sup = 0;
                foreach (var e in Entradas(C(t).JuntaArriba)) { ud += e.UDCalculo; sup += e.SuperficieCalculo; }
                t.UDCalculo = Math.Max(t.UD, ud);
                t.SuperficieCalculo = Math.Max(t.Superficie, sup);
                // Holgura del 5 %: el redondeo de MagiCAD no debe disparar el aviso.
                if (t.UD < 0.95 * ud - 1e-6 || t.Superficie < 0.95 * sup - 1e-6) C(t).CargaDesactualizada = true;
            }
            List<TramoHs5> Salidas(string j) =>
                j != null && enJunta.TryGetValue(j, out var l) ? l.Where(x => C(x).JuntaArriba == j).ToList() : new List<TramoHs5>();

            // Tramos cortos: pendiente del tramo horizontal largo más próximo del mismo recorrido
            // (primero aguas abajo, luego aguas arriba).
            foreach (var t in tramos.Where(x => !x.EsVertical && x.LongHorizontal < LongitudMinimaPendiente))
            {
                var vistos = new HashSet<long> { t.Id };
                var cola = new Queue<TramoHs5>();
                void Encolar(IEnumerable<TramoHs5> ts)
                {
                    foreach (var v in ts) if (!v.EsVertical && vistos.Add(v.Id)) cola.Enqueue(v);
                }
                Encolar(Salidas(C(t).JuntaAbajo));
                Encolar(Entradas(C(t).JuntaArriba));
                while (cola.Count > 0)
                {
                    var v = cola.Dequeue();
                    if (v.LongHorizontal >= LongitudMinimaPendiente)
                    {
                        t.PendienteCalculoPct = v.PendientePct;
                        C(t).PendienteDe = v.Id;
                        break;
                    }
                    Encolar(Salidas(C(v).JuntaAbajo));
                    Encolar(Entradas(C(v).JuntaArriba));
                }
            }

            // ── 2. Columnas verticales → bajantes (o caídas cortas) ──
            var visitados = new HashSet<long>();
            foreach (var t in tramos.Where(x => x.EsVertical))
            {
                if (!visitados.Add(t.Id)) continue;
                var columna = Componente(t, x => x.EsVertical, enJunta, visitados);

                double zMin = columna.Min(x => x.ZMin), zMax = columna.Max(x => x.ZMax);
                if (zMax - zMin < longitudMinimaBajante)
                {
                    foreach (var x in columna) C(x).EsCaidaCorta = true; // queda como Ramal
                    continue;
                }
                var b = new BajanteHs5
                {
                    Indice = red.Bajantes.Count,
                    Red = columna.GroupBy(x => x.Red).OrderByDescending(g => g.Count()).First().Key,
                    ZMin = zMin, ZMax = zMax,
                    UDMax = columna.Max(x => x.UDCalculo),
                    SuperficieMax = columna.Max(x => x.SuperficieCalculo),
                    DNActualMax = columna.Max(x => x.DNActual),
                };
                foreach (var x in columna)
                {
                    b.Tramos.Add(x.Id);
                    if (x.Junta0 != null) b.Juntas.Add(x.Junta0);
                    if (x.Junta1 != null) b.Juntas.Add(x.Junta1);
                    C(x).Tipo = TipoTramo.Bajante;
                    C(x).Bajante = b.Indice;
                }
                red.Bajantes.Add(b);
            }
            bool EsDeBajante(TramoHs5 x) => C(x).Tipo == TipoTramo.Bajante;

            // ── 3. Desviaciones: cadena horizontal sin ramificar bajante → bajante ──
            foreach (var h in tramos.Where(x => !x.EsVertical))
            {
                if (C(h).Tipo != TipoTramo.Ramal || C(h).Desviacion >= 0) continue;
                var origen = Entradas(C(h).JuntaArriba).FirstOrDefault(EsDeBajante);
                if (origen == null) continue;

                var cadena = new List<TramoHs5> { h };
                var actual = h;
                TramoHs5 destino = null;
                while (true)
                {
                    string j = C(actual).JuntaAbajo;
                    var salidas = Salidas(j);
                    if (Entradas(j).Count != 1) break;          // otro ramal acomete: es colector
                    var bajanteAbajo = salidas.FirstOrDefault(x => EsDeBajante(x) && C(x).Bajante != C(origen).Bajante);
                    if (bajanteAbajo != null) { destino = bajanteAbajo; break; }
                    if (salidas.Count != 1 || salidas[0].EsVertical || cadena.Contains(salidas[0])) break;
                    actual = salidas[0];
                    cadena.Add(actual);
                }
                if (destino == null) continue;

                var d = new DesviacionHs5
                {
                    Indice = red.Desviaciones.Count,
                    BajanteArriba = C(origen).Bajante,
                    BajanteAbajo = C(destino).Bajante,
                };
                foreach (var x in cadena)
                {
                    d.Tramos.Add(x.Id);
                    C(x).Tipo = TipoTramo.Desviacion;
                    C(x).Desviacion = d.Indice;
                }
                red.Desviaciones.Add(d);
                red.Bajantes[d.BajanteAbajo].DesviacionEntrada = d.Indice;
            }

            // ── 4. Carga de ramales y plantas de cada bajante ──
            foreach (var b in red.Bajantes)
            {
                var acometidas = b.Juntas.SelectMany(Entradas)
                    .Where(x => !EsDeBajante(x) && C(x).Tipo != TipoTramo.Desviacion)
                    .Distinct().ToList();
                b.UDRamalMax = acometidas.Count > 0 ? acometidas.Max(x => x.UDCalculo) : 0;

                if (nivelesZ == null || nivelesZ.Count == 0) continue;
                foreach (var x in acometidas)
                {
                    double z = C(x).JuntaAbajo == x.Junta0 ? x.Z0 : x.Z1;
                    b.NivelesVertido.Add(NivelMasProximo(nivelesZ, z));
                }
                if (b.NivelesVertido.Count == 0) // sin acometidas: niveles que atraviesa
                    for (int i = 0; i < nivelesZ.Count; i++)
                        if (nivelesZ[i] > b.ZMin + TolZ && nivelesZ[i] < b.ZMax - TolZ) b.NivelesVertido.Add(i);
            }
            // Bajo una desviación vierten también las plantas de la bajante superior.
            foreach (var b in red.Bajantes.OrderByDescending(x => x.ZMax))
                if (b.DesviacionEntrada >= 0)
                    b.NivelesVertido.UnionWith(red.Bajantes[red.Desviaciones[b.DesviacionEntrada].BajanteArriba].NivelesVertido);
            foreach (var b in red.Bajantes)
                b.Plantas = b.NivelesVertido.Count;

            // ── 5. Derivaciones individuales: del aparato hasta la primera unión ──
            foreach (string ja in aparatos)
                foreach (var inicio in Salidas(ja).Where(x => x.Red != RedHs5Tipo.Pluvial && !EsDeBajante(x)))
                {
                    if (C(inicio).Derivacion >= 0) continue;
                    var der = new DerivacionHs5 { Indice = red.Derivaciones.Count, JuntaAparato = ja };
                    var actual = inicio;
                    while (actual != null && C(actual).Derivacion < 0 && !EsDeBajante(actual)
                           && C(actual).Tipo != TipoTramo.Desviacion)
                    {
                        der.Tramos.Add(actual.Id);
                        der.Longitud += actual.Longitud;
                        C(actual).Tipo = TipoTramo.Derivacion;
                        C(actual).Derivacion = der.Indice;

                        // Sigue mientras nadie más acometa: un único tramo entra y otro sale.
                        string j = C(actual).JuntaAbajo;
                        var salidas = Salidas(j);
                        actual = j != null && !aparatos.Contains(j) && Entradas(j).Count == 1 && salidas.Count == 1
                            ? salidas[0] : null;
                    }
                    red.Derivaciones.Add(der);
                }

            // ── 6. Horizontales: colector si hay bajante aguas arriba, si no ramal ──
            var memo = new Dictionary<long, bool>();
            bool BajanteAguasArriba(TramoHs5 t, HashSet<long> enCurso)
            {
                if (memo.TryGetValue(t.Id, out bool r)) return r;
                if (!enCurso.Add(t.Id)) return false; // ciclo
                r = false;
                foreach (var e in Entradas(C(t).JuntaArriba))
                {
                    var ce = C(e);
                    if (ce.Tipo == TipoTramo.Bajante || ce.Tipo == TipoTramo.Desviacion ||
                        ce.Tipo == TipoTramo.Colector || BajanteAguasArriba(e, enCurso))
                    { r = true; break; }
                }
                enCurso.Remove(t.Id);
                memo[t.Id] = r;
                return r;
            }
            foreach (var t in tramos)
            {
                var c = C(t);
                if (c.Tipo != TipoTramo.Ramal || c.EsCaidaCorta) continue;
                if (BajanteAguasArriba(t, new HashSet<long>())) c.Tipo = TipoTramo.Colector;
            }

            // ── 7. Ventilación ──
            ClasificarVentilacion(todos.Where(t => t.EsVentilacion).ToList(), red, enJunta, porId);
            return red;
        }

        private static void ClasificarVentilacion(List<TramoHs5> vent, RedHs5 red,
                                                  Dictionary<string, List<TramoHs5>> desagueEnJunta,
                                                  Dictionary<long, TramoHs5> porId)
        {
            if (vent.Count == 0) return;
            var ventEnJunta = IndexarPorJunta(vent);
            List<TramoHs5> Desague(string j) =>
                j != null && desagueEnJunta.TryGetValue(j, out var l) ? l : new List<TramoHs5>();
            bool EsRamal(TramoHs5 x)
            {
                var t = red.Tramos[x.Id].Tipo;
                return t == TipoTramo.Ramal || t == TipoTramo.Derivacion || t == TipoTramo.Colector;
            }
            IEnumerable<string> Juntas(IEnumerable<TramoHs5> ts) =>
                ts.SelectMany(x => new[] { x.Junta0, x.Junta1 }).Where(j => j != null).Distinct();

            void Registrar(VentilacionHs5 v, IEnumerable<TramoHs5> ts)
            {
                v.Indice = red.Ventilaciones.Count;
                foreach (var x in ts)
                {
                    v.Tramos.Add(x.Id);
                    v.Longitud += x.Longitud;
                    red.Tramos[x.Id] = new ClasificacionTramo { Tipo = v.Tipo, Ventilacion = v.Indice };
                }
                red.Ventilaciones.Add(v);
            }

            var visitados = new HashSet<long>();
            foreach (var inicio in vent)
            {
                if (!visitados.Add(inicio.Id)) continue;
                var comp = Componente(inicio, _ => true, ventEnJunta, visitados);

                // Uniones a bajantes del componente: bajante → juntas de unión.
                var uniones = new Dictionary<int, HashSet<string>>();
                foreach (string j in Juntas(comp))
                    foreach (var d in Desague(j).Where(x => red.Tramos[x.Id].Tipo == TipoTramo.Bajante))
                    {
                        int b = red.Tramos[d.Id].Bajante;
                        if (!uniones.TryGetValue(b, out var s)) uniones[b] = s = new HashSet<string>();
                        s.Add(j);
                    }

                // Ramales de ventilación terciaria: cadenas horizontales que tocan un
                // ramal de desagüe sin tocar la bajante. Sin bajante, todo es terciaria.
                var terciarias = new List<List<TramoHs5>>();
                if (uniones.Count == 0)
                    terciarias.Add(comp);
                else
                {
                    var vistosH = new HashSet<long>();
                    foreach (var h in comp.Where(x => !x.EsVertical))
                    {
                        if (!vistosH.Add(h.Id)) continue;
                        var cadena = Componente(h, x => !x.EsVertical && comp.Contains(x), ventEnJunta, vistosH);
                        var js = Juntas(cadena).ToList();
                        bool tocaRamal = js.Any(j => Desague(j).Any(EsRamal));
                        bool tocaBajante = js.Any(j => uniones.Values.Any(s => s.Contains(j)));
                        if (tocaRamal && !tocaBajante) terciarias.Add(cadena);
                    }
                }

                foreach (var cadena in terciarias)
                {
                    var ramal = Juntas(cadena).SelectMany(Desague).Where(EsRamal)
                        .OrderByDescending(x => x.DNActual).FirstOrDefault();
                    Registrar(new VentilacionHs5 { Tipo = TipoTramo.VentTerciaria, RamalVentilado = ramal?.Id ?? -1 }, cadena);
                }

                var columna = comp.Where(x => !terciarias.Any(c => c.Contains(x))).ToList();
                if (columna.Count == 0) continue;
                // Bajante principal: la de más uniones.
                var (bajante, juntasUnion) = uniones.OrderByDescending(kv => kv.Value.Count)
                    .Select(kv => (kv.Key, kv.Value)).First();
                var bj = red.Bajantes[bajante];
                bool enCabeza = juntasUnion.Count == 1 && uniones.Count == 1 &&
                    columna.Any(x => (x.Junta0 == juntasUnion.First() ? x.Z0 : x.Junta1 == juntasUnion.First() ? x.Z1 : double.NegativeInfinity)
                                     >= bj.ZMax - TolCabezaBajante);
                Registrar(new VentilacionHs5
                {
                    Tipo = enCabeza ? TipoTramo.VentPrimaria : TipoTramo.VentSecundaria,
                    Bajante = bajante,
                    UnionesBajante = juntasUnion.Count,
                }, columna);
            }
        }

        /// <summary>
        /// Orienta cada red conexa hacia su salida: el extremo libre (sin más tramos ni aparato)
        /// más bajo; a igual cota (±1 mm), el de más carga. Una red de evacuación es un árbol,
        /// así que el sentido sale de la topología y no depende de pendientes dibujadas al
        /// milímetro ni de parámetros sin recalcular. Devuelve el extremo de aguas abajo de
        /// cada tramo y el orden de recorrido desde la salida.
        /// </summary>
        private static Dictionary<long, int> OrientarRed(List<TramoHs5> tramos, Dictionary<string, List<TramoHs5>> enJunta,
                                                         ICollection<string> aparatos, out List<TramoHs5> orden)
        {
            var abajo = new Dictionary<long, int>();
            orden = new List<TramoHs5>();
            string J(TramoHs5 t, int k) => k == 0 ? t.Junta0 : t.Junta1;
            double Z(TramoHs5 t, int k) => k == 0 ? t.Z0 : t.Z1;
            bool Libre(TramoHs5 t, int k)
            {
                string j = J(t, k);
                if (j == null) return true;
                if (aparatos.Contains(j)) return false;
                return enJunta.TryGetValue(j, out var l) && l.Count == 1;
            }

            var vistos = new HashSet<long>();
            foreach (var semilla in tramos)
            {
                if (vistos.Contains(semilla.Id)) continue;
                var comp = Componente(semilla, _ => true, enJunta, vistos);

                TramoHs5 st = null; int sk = 0;
                foreach (var t in comp)
                    for (int k = 0; k < 2; k++)
                    {
                        if (!Libre(t, k)) continue;
                        if (st == null) { st = t; sk = k; continue; }
                        double dz = Z(t, k) - Z(st, sk);
                        bool masBajo = dz < -TolZ;
                        bool igualYMasCarga = Math.Abs(dz) <= TolZ && t.UD + t.Superficie > st.UD + st.Superficie;
                        if (masBajo || igualYMasCarga) { st = t; sk = k; }
                    }
                if (st == null) continue; // red cerrada en bucle: se orienta por cotas

                abajo[st.Id] = sk;
                orden.Add(st);
                var cola = new Queue<TramoHs5>();
                cola.Enqueue(st);
                while (cola.Count > 0)
                {
                    var t = cola.Dequeue();
                    string arriba = J(t, 1 - abajo[t.Id]);
                    if (arriba == null || !enJunta.TryGetValue(arriba, out var l)) continue;
                    foreach (var v in l)
                    {
                        if (abajo.ContainsKey(v.Id)) continue;
                        abajo[v.Id] = v.Junta0 == arriba ? 0 : 1;
                        orden.Add(v);
                        cola.Enqueue(v);
                    }
                }
            }
            return abajo;
        }

        private static Dictionary<string, List<TramoHs5>> IndexarPorJunta(IEnumerable<TramoHs5> tramos)
        {
            var enJunta = new Dictionary<string, List<TramoHs5>>();
            foreach (var t in tramos)
                foreach (var j in new[] { t.Junta0, t.Junta1 })
                {
                    if (j == null) continue;
                    if (!enJunta.TryGetValue(j, out var l)) enJunta[j] = l = new List<TramoHs5>();
                    if (!l.Contains(t)) l.Add(t);
                }
            return enJunta;
        }

        /// <summary>Tramos conectados a <paramref name="inicio"/> por juntas que cumplen el filtro.</summary>
        private static List<TramoHs5> Componente(TramoHs5 inicio, Func<TramoHs5, bool> filtro,
                                                 Dictionary<string, List<TramoHs5>> enJunta, HashSet<long> visitados)
        {
            visitados.Add(inicio.Id);
            var comp = new List<TramoHs5> { inicio };
            var cola = new Queue<TramoHs5>(comp);
            while (cola.Count > 0)
            {
                var a = cola.Dequeue();
                foreach (var j in new[] { a.Junta0, a.Junta1 })
                {
                    if (j == null || !enJunta.TryGetValue(j, out var l)) continue;
                    foreach (var v in l)
                        if (v != a && filtro(v) && visitados.Add(v.Id)) { comp.Add(v); cola.Enqueue(v); }
                }
            }
            return comp;
        }

        /// <summary>
        /// 0 ó 1: extremo por el que sale el agua. Con desnivel, el más bajo; en
        /// tramos planos decide la UD acumulada de los tramos vecinos (aguas abajo
        /// la UD crece).
        /// </summary>
        internal static int ExtremoAguasAbajo(TramoHs5 t, List<TramoHs5> vecinos0, List<TramoHs5> vecinos1)
        {
            if (t.Dz > TolZ) return t.Z0 < t.Z1 ? 0 : 1;

            double Carga(TramoHs5 x) => x.UD + x.Superficie;
            double propia = Carga(t);
            double m0 = vecinos0.Count > 0 ? vecinos0.Max(Carga) : double.NegativeInfinity;
            double m1 = vecinos1.Count > 0 ? vecinos1.Max(Carga) : double.NegativeInfinity;
            bool mayor0 = m0 > propia, mayor1 = m1 > propia;
            if (mayor1 && !mayor0) return 1;
            if (mayor0 && !mayor1) return 0;
            // Extremo libre o aparato (sin tubos): es el de cabecera.
            if (vecinos0.Count == 0 && vecinos1.Count > 0) return 1;
            if (vecinos1.Count == 0 && vecinos0.Count > 0) return 0;
            return m0 > m1 ? 0 : 1;
        }

        private static int NivelMasProximo(IList<double> nivelesZ, double z)
        {
            int mejor = 0;
            for (int i = 1; i < nivelesZ.Count; i++)
                if (Math.Abs(nivelesZ[i] - z) < Math.Abs(nivelesZ[mejor] - z)) mejor = i;
            return mejor;
        }
    }
}
