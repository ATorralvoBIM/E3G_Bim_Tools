using System;
using System.Collections.Generic;
using System.Linq;

// =============================================================================
// CTE DB-HS5 — EVALUACIÓN DE UNA RED CLASIFICADA
// -----------------------------------------------------------------------------
// Sin dependencia de Revit. Asigna a cada tramo la tabla que le corresponde
// (modo automático) o la elegida a mano, y calcula el DN mínimo:
//   Residuales
//   · Derivación  → 4.1: Ø mínimo del aparato y sus UD, hasta la primera unión.
//   · Ramal       → 4.3 por pendiente (caída corta vertical → columna 4 %).
//   · Bajante     → 4.4: mayor DN entre UD de la bajante y UD máx. por ramal
//                   (4.1.2.2); bajo una desviación, ≥ DN de la desviación.
//   · Desviación  → 4.5 al 4 %, ≥ DN del tramo de bajante superior (4.1.2.3 b).
//   · Colector    → 4.5 por pendiente.
//   Pluviales (superficie × f, Anejo B) y mixtos (+ superficie equivalente, 4.3)
//   · Bajante     → 4.8.   · Ramal / colector / desviación → 4.9.
//   Ventilación
//   · Primaria    → mismo DN que la bajante (4.4.1).
//   · Secundaria  → 4.11 si se une en cada planta, si no 4.10; ≥ ½ DN bajante.
//   · Terciaria   → 4.12 según DN y pendiente del ramal ventilado.
//   Arquetas      → 4.13 según DN del colector de salida.
// Con NoDisminuirAguasAbajo, el DN exigido se propaga aguas abajo y se marca
// KO todo tramo más estrecho que alguno de los que le acometen.
// =============================================================================
namespace E3G.Saneamiento.Hs5
{
    public sealed class EvaluacionHs5
    {
        public long Id { get; set; }
        public TipoTramo Tipo { get; set; }
        public RedHs5Tipo Red { get; set; }
        public string TablaId { get; set; }
        public string Columna { get; set; }
        public int DNActual { get; set; }
        public int DNMinimo { get; set; }
        /// <summary>Texto del valor exigido cuando no es un DN (arquetas: "≥ 60×60 cm").</summary>
        public string TextoExigido { get; set; }
        public bool FueraDeTabla { get; set; }
        public bool PendienteInsuficiente { get; set; }
        /// <summary>Derivación: la UD del modelo es menor que la del aparato (tabla 4.1).</summary>
        public bool UDInsuficiente { get; set; }
        /// <summary>El DN del modelo es menor que el de algún tramo aguas arriba.</summary>
        public bool ReduccionAguasAbajo { get; set; }
        /// <summary>Arqueta con dimensiones menores que las de la tabla 4.13 o no leídas.</summary>
        public bool DimensionInsuficiente { get; set; }
        public bool EsCorrecto { get; set; }
        /// <summary>Explicación corta del cálculo, para la tabla de resultados.</summary>
        public string Criterio { get; set; }
        public List<string> Avisos { get; } = new List<string>();
    }

    /// <summary>Arqueta conectada a la red (dimensiones interiores en cm; NaN = no leídas).</summary>
    public sealed class ArquetaHs5
    {
        public long Id { get; set; }
        public string Junta { get; set; }
        public double LargoCm { get; set; } = double.NaN;
        public double AnchoCm { get; set; } = double.NaN;
    }

    public static class Hs5Evaluador
    {
        /// <summary>
        /// Evalúa los tramos de <paramref name="ids"/> y las arquetas que tocan alguno
        /// de ellos. Los que no encajan con la tabla elegida a mano (orientación o red)
        /// se cuentan en <paramref name="omitidos"/>.
        /// </summary>
        public static List<EvaluacionHs5> Evaluar(IList<TramoHs5> tramos, RedHs5 red, Hs5Config cfg,
                                                  ICollection<long> ids, out int omitidos,
                                                  IDictionary<string, List<AparatoConectado>> aparatos = null,
                                                  IList<ArquetaHs5> arquetas = null)
        {
            var porId = tramos.ToDictionary(t => t.Id);
            aparatos ??= new Dictionary<string, List<AparatoConectado>>();
            double f = cfg.FactorPluviometrico;

            // 1. DN base de cada tramo de desagüe de la red (no sólo los pedidos: la
            //    propagación aguas abajo necesita toda la red).
            var bajantes = DimensionarBajantes(porId, red, cfg, f);
            var evals = new Dictionary<long, EvaluacionHs5>();
            omitidos = 0;
            var pedidos = new HashSet<long>(ids);
            foreach (var t in tramos.Where(x => !x.EsVentilacion))
            {
                if (!red.Tramos.TryGetValue(t.Id, out var c)) continue;
                var ev = cfg.EsAutomatico
                    ? EvaluarAutomatico(t, c, red, bajantes, aparatos, cfg, f)
                    : EvaluarManual(t, c, bajantes, cfg, f);
                if (ev == null) { if (pedidos.Contains(t.Id)) omitidos++; continue; }
                if (c.Contrapendiente)
                {
                    ev.PendienteInsuficiente = true;
                    ev.Avisos.Add("contrapendiente: el tramo sube en el sentido del agua");
                }
                if (c.PendienteDe >= 0 && !t.EsVertical && cfg.ColumnaPendiente < 0)
                    ev.Criterio += $" · pend. {t.PendienteCalculoPct:0.0} % del tramo {c.PendienteDe} (tramo de {t.LongHorizontal * 100:0} cm)";
                if (c.CargaDesactualizada)
                    ev.Avisos.Add(t.Red == RedHs5Tipo.Pluvial
                        ? $"superficie del parámetro {Fmt(t.Superficie)} m² < la que le llega aguas arriba {Fmt(t.SuperficieCalculo)} m²: se usa esta (¿recalcular MagiCAD?)"
                        : $"UD del parámetro {Fmt(t.UD)} < las que le llegan aguas arriba {Fmt(t.UDCalculo)}: se usan estas (¿recalcular MagiCAD?)");
                evals[t.Id] = ev;
            }

            // 2. No disminuir aguas abajo.
            if (cfg.NoDisminuirAguasAbajo)
                PropagarAguasAbajo(tramos, red, evals, porId);

            // 3. Ventilación y arquetas (sólo en automático).
            if (cfg.EsAutomatico)
            {
                foreach (var v in red.Ventilaciones)
                    foreach (long id in v.Tramos)
                        evals[id] = EvaluarVentilacion(porId[id], v, red, evals, porId);
            }

            var res = new List<EvaluacionHs5>();
            foreach (long id in ids)
                if (evals.TryGetValue(id, out var ev)) res.Add(Cerrar(ev));
                else if (porId.TryGetValue(id, out var t) && t.EsVentilacion && !cfg.EsAutomatico) omitidos++;

            if (cfg.EsAutomatico && arquetas != null)
                foreach (var a in arquetas)
                {
                    var salidas = tramos.Where(t => !t.EsVentilacion && red.Tramos.TryGetValue(t.Id, out var c) && c.JuntaArriba == a.Junta).ToList();
                    var tocan = tramos.Where(t => (t.Junta0 == a.Junta || t.Junta1 == a.Junta) && pedidos.Contains(t.Id));
                    if (!tocan.Any()) continue;
                    res.Add(Cerrar(EvaluarArqueta(a, salidas, evals)));
                }
            return res;
        }

        // ── Automático ──

        private static EvaluacionHs5 EvaluarAutomatico(TramoHs5 t, ClasificacionTramo c, RedHs5 red,
            ResultadoBajantes baj, IDictionary<string, List<AparatoConectado>> aparatos, Hs5Config cfg, double f)
        {
            var ev = Nueva(t, c.Tipo);
            switch (c.Tipo)
            {
                case TipoTramo.Derivacion:
                {
                    var der = red.Derivaciones[c.Derivacion];
                    aparatos.TryGetValue(der.JuntaAparato, out var lista);
                    var validos = (lista ?? new List<AparatoConectado>()).Where(a => !a.Ignorado).ToList();
                    if (validos.Count == 0)
                    {
                        // Aparato ignorado o desconocido: se comprueba como ramal.
                        ev.Tipo = TipoTramo.Ramal;
                        return PorPendiente(ev, t, Hs5Tablas.T4_3, t.UDCalculo, cfg);
                    }
                    double udAparatos = validos.Sum(a => a.UD);
                    double udExigida = validos.Where(a => a.ComprobarUD).Sum(a => a.UD);
                    bool publico = validos.Any(a => a.UsoPublico);
                    ev.TablaId = validos.All(a => !a.ComprobarUD) ? "4.2" : "4.1";
                    ev.Columna = publico ? "Uso público" : "Uso privado";
                    ev.DNMinimo = validos.Max(a => a.DN);
                    ev.Criterio = $"{ev.TablaId} · {string.Join(" + ", validos.Select(a => a.Nombre))} · " +
                                  $"{(publico ? "público" : "privado")} · UD {Fmt(udAparatos)} → ø{ev.DNMinimo}";
                    if (udExigida > 0 && t.UD + 1e-6 < udExigida)
                    {
                        ev.UDInsuficiente = true;
                        ev.Avisos.Add($"UD del modelo {Fmt(t.UD)} < UD del aparato {Fmt(udExigida)}");
                    }
                    if (der.Longitud > 1.5)
                        ev.Avisos.Add($"derivación de {der.Longitud:0.0} m (> 1,5 m): requiere cálculo pormenorizado (4.1.1.1.3)");
                    foreach (var a in validos.Where(a => a.Aviso != null)) ev.Avisos.Add($"{a.Nombre}: {a.Aviso}");
                    return ev;
                }
                case TipoTramo.Bajante:
                {
                    var b = baj.Bajante[c.Bajante];
                    ev.TablaId = b.TablaId; ev.Columna = b.Columna; ev.DNMinimo = b.DN;
                    ev.FueraDeTabla = b.Fuera; ev.Criterio = b.Criterio;
                    return ev;
                }
                case TipoTramo.Desviacion when t.Red != RedHs5Tipo.Pluvial:
                {
                    var d = baj.Desviacion[c.Desviacion];
                    ev.TablaId = "4.5"; ev.Columna = "4 %"; ev.DNMinimo = d.DN;
                    ev.FueraDeTabla = d.Fuera; ev.Criterio = d.Criterio;
                    return ev;
                }
                default:
                    if (t.Red == RedHs5Tipo.Residual)
                        return PorPendiente(ev, t, c.Tipo == TipoTramo.Colector ? Hs5Tablas.T4_5 : Hs5Tablas.T4_3, t.UDCalculo, cfg);
                    // Pluvial o mixta: tabla 4.9 con la superficie corregida.
                    return PorPendiente(ev, t, Hs5Tablas.T4_9, SuperficieCorregida(t, f), cfg, SufijoSuperficie(t, f));
            }
        }

        // ── Manual: la tabla elegida para todos los tramos compatibles ──

        private static EvaluacionHs5 EvaluarManual(TramoHs5 t, ClasificacionTramo c, ResultadoBajantes baj, Hs5Config cfg, double f)
        {
            var tabla = cfg.Tabla;
            bool tablaVertical = tabla.Id == Hs5Tablas.T4_4.Id || tabla.Id == Hs5Tablas.T4_8.Id;
            bool porUD = tabla.Magnitud == "UD";
            if (tablaVertical != t.EsVertical) return null;
            if (porUD ? t.Red == RedHs5Tipo.Pluvial : t.Red == RedHs5Tipo.Residual) return null;

            var ev = Nueva(t, c.Tipo);
            if (tablaVertical)
            {
                if (c.Bajante >= 0 && baj.Bajante[c.Bajante].TablaId == tabla.Id)
                {
                    var b = baj.Bajante[c.Bajante];
                    ev.TablaId = b.TablaId; ev.Columna = b.Columna; ev.DNMinimo = b.DN;
                    ev.FueraDeTabla = b.Fuera; ev.Criterio = b.Criterio;
                    return ev;
                }
                int col = tabla.Id == Hs5Tablas.T4_4.Id
                    ? Hs5Tablas.ColumnaBajante(cfg.ModoAltura == Hs5Config.AlturaMasDe3 ? 4 : 1, false) : 0;
                double v = porUD ? t.UDCalculo : SuperficieCorregida(t, f);
                var q = tabla.Consultar(v, col);
                ev.TablaId = tabla.Id; ev.Columna = tabla.Columnas[col];
                ev.DNMinimo = q.DNMinimo; ev.FueraDeTabla = q.FueraDeTabla;
                ev.Criterio = $"{tabla.Id} · {ev.Columna} · {(porUD ? $"UD {Fmt(v)}" : $"{Fmt(v)} m²")}";
                return ev;
            }
            return PorPendiente(ev, t, tabla, porUD ? t.UDCalculo : SuperficieCorregida(t, f), cfg, porUD ? "" : SufijoSuperficie(t, f));
        }

        private static EvaluacionHs5 PorPendiente(EvaluacionHs5 ev, TramoHs5 t, TablaHs5 tabla, double valor,
                                                  Hs5Config cfg, string sufijo = "")
        {
            bool insuficiente = false;
            int col = cfg.ColumnaPendiente >= 0 && cfg.ColumnaPendiente < tabla.Columnas.Length
                ? cfg.ColumnaPendiente
                : t.EsVertical ? tabla.Columnas.Length - 1
                : tabla.ColumnaPorPendiente(t.PendienteCalculoPct, out insuficiente);
            var q = tabla.Consultar(valor, col);
            ev.TablaId = tabla.Id;
            ev.Columna = tabla.Columnas[col];
            ev.DNMinimo = q.DNMinimo;
            ev.FueraDeTabla = q.FueraDeTabla;
            ev.PendienteInsuficiente = insuficiente;
            ev.Criterio = $"{tabla.Id} · {ev.Columna}{(t.EsVertical ? " (caída)" : "")}{sufijo}";
            return ev;
        }

        // ── Bajantes y desviaciones (de arriba abajo) ──

        private sealed class DnBajante { public string TablaId, Columna, Criterio; public int DN; public bool Fuera; }
        private sealed class ResultadoBajantes
        {
            public DnBajante[] Bajante;
            public DnBajante[] Desviacion;
        }

        private static ResultadoBajantes DimensionarBajantes(Dictionary<long, TramoHs5> porId, RedHs5 red, Hs5Config cfg, double f)
        {
            var r = new ResultadoBajantes
            {
                Bajante = new DnBajante[red.Bajantes.Count],
                Desviacion = new DnBajante[red.Desviaciones.Count],
            };
            var t44 = Hs5Tablas.T4_4;

            // De arriba abajo: la bajante superior fija el mínimo de su desviación, y ésta el de la inferior.
            foreach (var b in red.Bajantes.OrderByDescending(x => x.ZMax))
            {
                DnBajante res;
                bool pluvial = b.Red == RedHs5Tipo.Pluvial || (b.Red == RedHs5Tipo.Mixta && b.UDMax <= 0);
                if (pluvial)
                {
                    double s = b.SuperficieMax * f;
                    var q = Hs5Tablas.T4_8.Consultar(s, 0);
                    res = new DnBajante
                    {
                        TablaId = "4.8", Columna = "100 mm/h", DN = q.DNMinimo, Fuera = q.FueraDeTabla,
                        Criterio = $"4.8 · {Fmt(b.SuperficieMax)} m² × f {f:0.##} = {Fmt(s)} m² → ø{q.DNMinimo}",
                    };
                }
                else
                {
                    int plantas = cfg.ModoAltura == Hs5Config.AlturaHasta3 ? 1
                                : cfg.ModoAltura == Hs5Config.AlturaMasDe3 ? 4
                                : b.Plantas;
                    string txtPlantas = cfg.ModoAltura == Hs5Config.AlturaDelModelo
                        ? (b.Plantas > 0 ? $"{b.Plantas} pl." : "pl. desconocidas")
                        : (plantas > 3 ? "> 3 pl." : "≤ 3 pl.");

                    int colB = Hs5Tablas.ColumnaBajante(plantas, false);
                    var qB = t44.Consultar(b.UDMax, colB);
                    res = new DnBajante
                    {
                        TablaId = "4.4", Columna = t44.Columnas[colB], DN = qB.DNMinimo, Fuera = qB.FueraDeTabla,
                        Criterio = $"4.4 · {txtPlantas} · UD {Fmt(b.UDMax)} → ø{qB.DNMinimo}",
                    };
                    if (b.UDRamalMax > 0)
                    {
                        var qR = t44.Consultar(b.UDRamalMax, Hs5Tablas.ColumnaBajante(plantas, true));
                        res.Criterio += $"; ramal UD {Fmt(b.UDRamalMax)} → ø{qR.DNMinimo}";
                        if (qR.DNMinimo > res.DN) res.DN = qR.DNMinimo;
                        res.Fuera |= qR.FueraDeTabla;
                    }
                }
                if (b.DesviacionEntrada >= 0 && r.Desviacion[b.DesviacionEntrada] != null
                    && r.Desviacion[b.DesviacionEntrada].DN > res.DN)
                {
                    res.DN = r.Desviacion[b.DesviacionEntrada].DN;
                    res.Criterio += $"; ≥ desviación ø{res.DN}";
                }
                r.Bajante[b.Indice] = res;

                // Desviaciones residuales que salen de esta bajante (las pluviales van por 4.9).
                foreach (var d in red.Desviaciones.Where(x => x.BajanteArriba == b.Indice))
                {
                    double ud = d.Tramos.Max(id => porId[id].UDCalculo);
                    var q = Hs5Tablas.T4_5.Consultar(ud, 2); // 4 %
                    int dnD = Math.Max(q.DNMinimo, res.DN);
                    r.Desviacion[d.Indice] = new DnBajante
                    {
                        TablaId = "4.5", Columna = "4 %", DN = dnD, Fuera = q.FueraDeTabla,
                        Criterio = $"Desviación · 4.5 al 4 % · UD {Fmt(ud)} → ø{q.DNMinimo}" +
                                   (res.DN > q.DNMinimo ? $"; ≥ bajante sup. ø{res.DN}" : ""),
                    };
                }
            }
            return r;
        }

        // ── No disminuir aguas abajo ──

        private static void PropagarAguasAbajo(IList<TramoHs5> tramos, RedHs5 red,
                                               Dictionary<long, EvaluacionHs5> evals, Dictionary<long, TramoHs5> porId)
        {
            var exigido = new Dictionary<long, int>();   // DN exigido propagado
            var actualArriba = new Dictionary<long, int>(); // mayor DN real aguas arriba (incluido)
            var enCurso = new HashSet<long>();

            (int Exigido, int Actual) Calcular(TramoHs5 t)
            {
                if (exigido.TryGetValue(t.Id, out int e)) return (e, actualArriba[t.Id]);
                if (!enCurso.Add(t.Id)) return (0, 0); // ciclo
                int ex = evals.TryGetValue(t.Id, out var ev) ? ev.DNMinimo : 0;
                int ac = t.DNActual;
                int maxExArriba = 0, maxAcArriba = 0;
                if (red.Tramos.TryGetValue(t.Id, out var c))
                    foreach (var arriba in red.Entradas(c.JuntaArriba))
                    {
                        var (exA, acA) = Calcular(arriba);
                        maxExArriba = Math.Max(maxExArriba, exA);
                        maxAcArriba = Math.Max(maxAcArriba, acA);
                    }
                enCurso.Remove(t.Id);

                if (ev != null)
                {
                    if (maxExArriba > ex)
                    {
                        ev.DNMinimo = maxExArriba;
                        ev.Criterio += $"; ≥ aguas arriba ø{maxExArriba}";
                    }
                    if (t.DNActual > 0 && maxAcArriba > t.DNActual)
                    {
                        ev.ReduccionAguasAbajo = true;
                        ev.Avisos.Add($"reduce de ø{maxAcArriba} aguas arriba a ø{t.DNActual}");
                    }
                }
                exigido[t.Id] = Math.Max(ex, maxExArriba);
                actualArriba[t.Id] = Math.Max(ac, maxAcArriba);
                return (exigido[t.Id], actualArriba[t.Id]);
            }

            foreach (var t in tramos.Where(x => !x.EsVentilacion)) Calcular(t);
        }

        // ── Ventilación ──

        private static EvaluacionHs5 EvaluarVentilacion(TramoHs5 t, VentilacionHs5 v, RedHs5 red,
                                                        Dictionary<long, EvaluacionHs5> evals,
                                                        Dictionary<long, TramoHs5> porId)
        {
            var ev = Nueva(t, v.Tipo);
            if (v.Tipo == TipoTramo.VentTerciaria)
            {
                if (v.RamalVentilado < 0 || !evals.TryGetValue(v.RamalVentilado, out var ramalEv))
                {
                    ev.Criterio = "Ventilación terciaria sin ramal de desagüe conectado";
                    ev.Avisos.Add("ventilación sin conexión a desagüe");
                    return ev;
                }
                int dnRamal = Math.Max(ramalEv.DNActual, ramalEv.DNMinimo);
                var tramoRamal = porId[v.RamalVentilado];
                double pend = tramoRamal.EsVertical ? 2.0 : tramoRamal.PendientePct;
                var q = Hs5Tablas.VentilacionTerciaria(dnRamal, pend, v.Longitud);
                ev.TablaId = "4.12"; ev.Columna = pend > 1.5 ? "2 %" : "1 %";
                ev.DNMinimo = q.DNMinimo; ev.FueraDeTabla = q.FueraDeTabla;
                ev.Criterio = $"4.12 · ramal ø{dnRamal} al {ev.Columna} · L {v.Longitud:0.0} m → ø{q.DNMinimo}";
                return ev;
            }

            var b = red.Bajantes[v.Bajante];
            int dnBajante = b.DNActualMax;
            var tramoBajante = b.Tramos.Select(id => evals.TryGetValue(id, out var e) ? e : null).FirstOrDefault(e => e != null);
            if (tramoBajante != null) dnBajante = Math.Max(dnBajante, tramoBajante.DNMinimo);

            if (v.Tipo == TipoTramo.VentPrimaria)
            {
                ev.TablaId = "4.4.1"; ev.Columna = "Primaria";
                ev.DNMinimo = dnBajante;
                ev.Criterio = $"4.4.1 · ventilación primaria = DN bajante ø{dnBajante}";
                return ev;
            }

            // Secundaria
            int mitad = (int)Math.Ceiling(dnBajante / 2.0);
            if (b.Plantas > 0 && v.UnionesBajante >= b.Plantas)
            {
                int dn = Hs5Tablas.VentilacionCadaPlanta(dnBajante);
                ev.TablaId = "4.11"; ev.Columna = "Unión en cada planta"; ev.DNMinimo = dn;
                ev.Criterio = $"4.11 · bajante ø{dnBajante} unida en {v.UnionesBajante} plantas → ø{dn}";
            }
            else
            {
                // Longitud efectiva aproximada por la longitud real de la columna.
                var q = Hs5Tablas.VentilacionSecundaria(dnBajante, b.UDMax, v.Longitud);
                ev.TablaId = "4.10"; ev.Columna = $"Bajante ø{dnBajante}";
                ev.DNMinimo = q.DNMinimo; ev.FueraDeTabla = q.FueraDeTabla;
                ev.Criterio = $"4.10 · bajante ø{dnBajante} · UD {Fmt(b.UDMax)} · L {v.Longitud:0.0} m → ø{q.DNMinimo}";
            }
            if (ev.DNMinimo < mitad)
            {
                int dnMitad = Hs5Tablas.T4_10_DNVentilacion.FirstOrDefault(d => d >= mitad);
                ev.DNMinimo = dnMitad > 0 ? dnMitad : mitad;
                ev.Criterio += $"; ≥ ½ bajante ø{ev.DNMinimo} (4.4.2.4)";
            }
            // 4.4.2.1: diámetro uniforme en todo el recorrido.
            if (v.Tramos.Select(id => porId[id].DNActual).Distinct().Count() > 1)
                ev.Avisos.Add("la columna de ventilación no tiene un diámetro uniforme (4.4.2.1)");
            return ev;
        }

        // ── Arquetas ──

        private static EvaluacionHs5 EvaluarArqueta(ArquetaHs5 a, List<TramoHs5> salidas, Dictionary<long, EvaluacionHs5> evals)
        {
            var ev = new EvaluacionHs5 { Id = a.Id, Tipo = TipoTramo.Arqueta, TablaId = "4.13", Columna = "L × A" };
            if (salidas.Count == 0)
            {
                ev.Criterio = "4.13 · arqueta sin colector de salida conectado";
                ev.Avisos.Add("sin colector de salida");
                ev.DimensionInsuficiente = true;
                return ev;
            }
            int dnSalida = salidas.Max(t => Math.Max(t.DNActual, evals.TryGetValue(t.Id, out var e) ? e.DNMinimo : 0));
            var (l, an, fuera) = Hs5Tablas.Arqueta(dnSalida);
            ev.DNActual = dnSalida;
            ev.DNMinimo = dnSalida;
            ev.FueraDeTabla = fuera;
            ev.TextoExigido = $"≥ {l}×{an} cm";
            if (double.IsNaN(a.LargoCm) || double.IsNaN(a.AnchoCm))
            {
                ev.DimensionInsuficiente = true;
                ev.Criterio = $"4.13 · salida ø{dnSalida} → {l}×{an} cm · dimensiones del modelo no leídas";
                ev.Avisos.Add("no se han podido leer las dimensiones de la arqueta");
                return ev;
            }
            double mayor = Math.Max(a.LargoCm, a.AnchoCm), menor = Math.Min(a.LargoCm, a.AnchoCm);
            ev.DimensionInsuficiente = mayor + 1e-6 < Math.Max(l, an) || menor + 1e-6 < Math.Min(l, an);
            ev.Criterio = $"4.13 · salida ø{dnSalida} → {l}×{an} cm · modelo {a.LargoCm:0}×{a.AnchoCm:0} cm";
            return ev;
        }

        // ── Utilidades ──

        private static EvaluacionHs5 Nueva(TramoHs5 t, TipoTramo tipo) =>
            new EvaluacionHs5 { Id = t.Id, Tipo = tipo, Red = t.Red, DNActual = t.DNActual };

        private static EvaluacionHs5 Cerrar(EvaluacionHs5 ev)
        {
            ev.EsCorrecto = !ev.FueraDeTabla && !ev.PendienteInsuficiente && !ev.UDInsuficiente
                         && !ev.ReduccionAguasAbajo && !ev.DimensionInsuficiente
                         && (ev.Tipo == TipoTramo.Arqueta || ev.DNActual >= ev.DNMinimo);
            return ev;
        }

        /// <summary>Superficie a 100 mm/h equivalente: (S + S_eq(UD)) × f.</summary>
        private static double SuperficieCorregida(TramoHs5 t, double f) =>
            (t.SuperficieCalculo + (t.Red == RedHs5Tipo.Mixta ? Hs5Tablas.SuperficieEquivalente(t.UDCalculo) : 0)) * f;

        private static string SufijoSuperficie(TramoHs5 t, double f) =>
            t.Red == RedHs5Tipo.Mixta
                ? $" · ({Fmt(t.SuperficieCalculo)} + {Fmt(Hs5Tablas.SuperficieEquivalente(t.UDCalculo))} eq.) m² × f {f:0.##}"
                : $" · {Fmt(t.SuperficieCalculo)} m² × f {f:0.##}";

        private static string Fmt(double v) => v.ToString("0.#", Hs5Tablas.Es);
    }
}
