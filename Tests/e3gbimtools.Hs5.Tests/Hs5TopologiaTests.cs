using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace E3G.Saneamiento.Hs5.Tests
{
    public class Hs5TopologiaTests
    {
        private static readonly double[] Niveles = { 0, 3, 6, 9, 12 };

        private static TramoHs5 T(long id, double ud, string j0, double z0, string j1, double z1, double lh) =>
            new TramoHs5 { Id = id, UD = ud, Junta0 = j0, Z0 = z0, Junta1 = j1, Z1 = z1, LongHorizontal = lh };

        /// <summary>
        /// Bajante de 4 tramos con ramales en tres plantas, una caída corta de
        /// aparato y un colector a pie de bajante.
        ///
        ///   (vent) V3 ── T3 ── V2 ── T2 ── V1 ── T1 ── V0 ── T0 ── C1 ──▶
        ///                │            │            │
        ///                R3 ─ A3      R2           R1
        ///                     │
        ///                     D (caída 0,45 m desde aparato X)
        /// </summary>
        private static List<TramoHs5> RedBajante() => new List<TramoHs5>
        {
            T(1, 0,  null, 12.5, "T3", 8.7, 0),   // V3 ventilación primaria
            T(2, 20, "T3", 8.7,  "T2", 5.7, 0),   // V2
            T(3, 40, "T2", 5.7,  "T1", 2.7, 0),   // V1
            T(4, 70, "T1", 2.7,  "T0", -0.3, 0),  // V0
            T(10, 20, "A3", 8.75, "T3", 8.7, 5),  // R3 al 1 %
            T(11, 20, null, 5.75, "T2", 5.7, 5),  // R2
            T(12, 30, null, 2.75, "T1", 2.7, 5),  // R1
            T(20, 20, "X", 9.2, "A3", 8.75, 0),   // D caída corta
            T(30, 70, "T0", -0.3, "J1", -0.35, 5),// C1 colector al 1 %
        };

        [Fact]
        public void Clasifica_BajanteRamalesColectorYCaida()
        {
            var red = Hs5Topologia.Clasificar(RedBajante(), Niveles);

            Assert.Single(red.Bajantes);
            foreach (long id in new long[] { 1, 2, 3, 4 })
                Assert.Equal(TipoTramo.Bajante, red.Tramos[id].Tipo);
            foreach (long id in new long[] { 10, 11, 12 })
                Assert.Equal(TipoTramo.Ramal, red.Tramos[id].Tipo);
            Assert.Equal(TipoTramo.Colector, red.Tramos[30].Tipo);

            Assert.Equal(TipoTramo.Ramal, red.Tramos[20].Tipo);
            Assert.True(red.Tramos[20].EsCaidaCorta);
        }

        [Fact]
        public void Bajante_CargaDeRamalesYPlantas()
        {
            var b = Hs5Topologia.Clasificar(RedBajante(), Niveles).Bajantes[0];
            Assert.Equal(70, b.UDMax);
            Assert.Equal(30, b.UDRamalMax);
            Assert.Equal(3, b.Plantas); // vierten las plantas 3, 6 y 9
        }

        [Fact]
        public void Evalua_ConLaTablaDeCadaTipo()
        {
            var tramos = RedBajante();
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var evals = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), tramos.Select(t => t.Id).ToList(), out int omitidos)
                .ToDictionary(e => e.Id);

            Assert.Equal(0, omitidos);
            // Bajante ≤ 3 pl.: UD 70 → ø90; ramal UD 30 → ø90.
            Assert.Equal("4.4", evals[4].TablaId);
            Assert.Equal(90, evals[4].DNMinimo);
            Assert.Equal(90, evals[1].DNMinimo); // toda la bajante con el mismo DN
            // Ramal al 1 %: 20 UD → ø90 (al 1 % no se admite menos).
            Assert.Equal("4.3", evals[10].TablaId);
            Assert.Equal("1 %", evals[10].Columna);
            Assert.Equal(90, evals[10].DNMinimo);
            // Caída corta vertical → 4.3 al 4 %: 20 UD → ø75.
            Assert.Equal("4 %", evals[20].Columna);
            Assert.Equal(75, evals[20].DNMinimo);
            // Colector al 1 %: 70 UD → ø90.
            Assert.Equal("4.5", evals[30].TablaId);
            Assert.Equal(90, evals[30].DNMinimo);
        }

        [Fact]
        public void Bajante_CriterioRamalPuedeMandar()
        {
            // Ramal de 100 UD a una bajante de 3 plantas: por UD de bajante (100) basta ø90,
            // pero por ramal (≤ 3 pl.) 100 UD exige ø110.
            var tramos = new List<TramoHs5>
            {
                T(1, 100, "A", 6, "B", 0, 0),
                T(2, 100, null, 6.05, "A", 6, 5),
            };
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 1 }, out _).Single();
            Assert.Equal(110, ev.DNMinimo);
            Assert.Contains("ramal UD 100", ev.Criterio);
        }

        [Fact]
        public void Bajante_MasDe3Plantas()
        {
            var cfg = new Hs5Config { ModoAltura = Hs5Config.AlturaMasDe3 };
            var tramos = RedBajante();
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var ev = Hs5Evaluador.Evaluar(tramos, red, cfg, new long[] { 4 }, out _).Single();
            // > 3 pl.: UD 70 → ø90 (≤ 280); ramal 30 → ø90 (≤ 53).
            Assert.Equal(90, ev.DNMinimo);
            Assert.Equal("Bajante > 3 pl.", ev.Columna);
        }

        /// <summary>
        ///   U1 ── U0 ──(J)── H1 ── H2 ──(K)── L1 ── L0 ──▶
        ///   bajante superior   desviación     bajante inferior
        /// </summary>
        private static List<TramoHs5> RedDesviacion() => new List<TramoHs5>
        {
            T(1, 300, null, 12, "U", 9, 0),
            T(2, 300, "U", 9, "J", 6, 0),
            T(3, 300, "J", 6, "M", 5.9, 2.5),   // H1 al 4 %
            T(4, 300, "M", 5.9, "K", 5.8, 2.5), // H2 al 4 %
            T(5, 300, "K", 5.8, "N", 3, 0),
            T(6, 320, "N", 3, "Z", 0, 0),
            T(7, 300, null, 9.05, "U", 9, 5),   // ramal a la bajante superior
            T(8, 20, null, 3.05, "N", 3, 5),    // ramal a la bajante inferior
        };

        [Fact]
        public void Detecta_Desviacion()
        {
            var red = Hs5Topologia.Clasificar(RedDesviacion(), Niveles);
            Assert.Equal(2, red.Bajantes.Count);
            Assert.Single(red.Desviaciones);
            Assert.Equal(TipoTramo.Desviacion, red.Tramos[3].Tipo);
            Assert.Equal(TipoTramo.Desviacion, red.Tramos[4].Tipo);

            var inferior = red.Bajantes[red.Tramos[6].Bajante];
            Assert.Equal(0, inferior.DesviacionEntrada);
            Assert.Equal(20, inferior.UDRamalMax);  // la desviación no cuenta como ramal
            Assert.Equal(2, inferior.Plantas);      // planta propia + la de la bajante superior
        }

        [Fact]
        public void Desviacion_NoMenorQueBajanteSuperiorYPropagaHaciaAbajo()
        {
            var tramos = RedDesviacion();
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), tramos.Select(t => t.Id).ToList(), out _)
                .ToDictionary(e => e.Id);

            // Superior (≤ 3 pl.): UD 300 → ø110; ramal 300 UD → ø160 (≤ 1.120).
            Assert.Equal(160, ev[1].DNMinimo);
            // Desviación al 4 %: 300 UD → ø110, pero no menor que la bajante superior.
            Assert.Equal(160, ev[3].DNMinimo);
            Assert.Equal("4 %", ev[3].Columna);
            // Inferior: por sí sola ø110, hereda ø160 de la desviación.
            Assert.Equal(160, ev[6].DNMinimo);
        }

        [Fact]
        public void TramoPlano_SentidoPorUD()
        {
            // P plano (Dz = 0) con un vecino de más UD en el extremo 1.
            var tramos = new List<TramoHs5>
            {
                T(1, 10, "A", 1, "B", 1, 4),
                T(2, 25, "B", 1, "C", 0.9, 4),
                T(3, 4, "D", 1.1, "A", 1, 4),
            };
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            Assert.Equal("B", red.Tramos[1].JuntaAbajo);
        }

        /// <summary>
        /// Caso real (proyecto 25-144, tramo 20310700): dos bajantes pluviales de 113,9 m²
        /// llegan a una te por tubos recién dibujados con el parámetro a 0; uno de ellos casi
        /// plano (0,7 mm) y otro con contrapendiente. El tramo tras la te es colector.
        ///
        ///  B1 ─ P1(plano, 0) ─ P2(0) ─┐
        ///                             TE ─ SEL(227,8) ─ OUT ▶
        ///  B2 ─ Q1(0) ─ Q2(sube 7 mm) ┘
        /// </summary>
        private static List<TramoHs5> RedPluvialRedibujada() => new List<TramoHs5>
        {
            T(1, 0, null, 17.4, "b1", 3.451, 0),                 // B1
            T(2, 0, "b1", 3.3285, "p", 3.3278, 0.07),            // P1 casi plano, parámetro 0
            T(3, 0, "p", 3.3278, "TE", 3.221, 8.0),              // P2 parámetro 0
            T(4, 0, null, 18.37, "b2", 3.377, 0),                // B2
            T(5, 0, "b2", 3.256, "q", 3.213, 4.35),              // Q1 parámetro 0
            T(6, 0, "q", 3.213, "TE", 3.220, 0.74),              // Q2 contrapendiente (+7 mm hacia la te)
            T(7, 0, "TE", 3.218, "o", 3.140, 7.8),               // SEL
            T(8, 0, "o", 3.140, null, 3.00, 6.0),                // salida
        }.Select(t =>
        {
            t.Red = RedHs5Tipo.Pluvial;
            t.Superficie = t.Id == 1 || t.Id == 4 ? 113.9 : t.Id >= 7 ? 227.8 : 0;
            return t;
        }).ToList();

        [Fact]
        public void RedRedibujada_ColectorTrasLasBajantes()
        {
            var tramos = RedPluvialRedibujada();
            var red = Hs5Topologia.Clasificar(tramos, Niveles);

            Assert.Equal(2, red.Bajantes.Count);
            Assert.Equal(TipoTramo.Colector, red.Tramos[7].Tipo);
            Assert.Equal(TipoTramo.Colector, red.Tramos[2].Tipo);   // el enlace casi plano va hacia la te
            Assert.Equal("p", red.Tramos[2].JuntaAbajo);
            Assert.True(red.Tramos[6].Contrapendiente);
            Assert.Equal("TE", red.Tramos[6].JuntaAbajo);          // el agua va a la te aunque suba
        }

        [Fact]
        public void RedRedibujada_CargaDeCalculoYAvisos()
        {
            var tramos = RedPluvialRedibujada();
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            Assert.Equal(113.9, tramos.Single(t => t.Id == 3).SuperficieCalculo, 6); // parámetro 0 → lo que le llega
            Assert.True(red.Tramos[3].CargaDesactualizada);
            Assert.False(red.Tramos[7].CargaDesactualizada);                         // 227,8 = 2 × 113,9

            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 3, 6 }, out _).ToDictionary(e => e.Id);
            Assert.Contains(ev[3].Avisos, a => a.Contains("recalcular"));
            Assert.True(ev[6].PendienteInsuficiente);
            Assert.Contains(ev[6].Avisos, a => a.Contains("contrapendiente"));
        }

        [Fact]
        public void TramoCorto_TomaLaPendienteDelContiguo()
        {
            // Manguito de 6 cm al 0,71 % entre dos tubos al 1 % (caso 20304377).
            var tramos = new List<TramoHs5>
            {
                T(1, 20, null, 1.0225, "a", 1.0000, 2.25),     // 1 %
                T(2, 20, "a", 1.0000, "b", 0.99957, 0.06),     // 0,71 %
                T(3, 20, "b", 0.99957, null, 0.98587, 1.37),   // 1 %
            };
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            Assert.Equal(3, red.Tramos[2].PendienteDe);
            Assert.Equal(1.0, tramos[1].PendienteCalculoPct, 2);

            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 2 }, out _).Single();
            Assert.False(ev.PendienteInsuficiente);
            Assert.Equal("1 %", ev.Columna);
            Assert.Contains("del tramo 3", ev.Criterio);
        }

        [Fact]
        public void ModoManual_OmiteOrientacionNoAplicable()
        {
            var tramos = RedBajante();
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var cfg = new Hs5Config { TablaId = "4.5" };
            var evals = Hs5Evaluador.Evaluar(tramos, red, cfg, tramos.Select(t => t.Id).ToList(), out int omitidos);
            Assert.Equal(5, omitidos); // 4 de bajante + la caída vertical
            Assert.All(evals, e => Assert.Equal("4.5", e.TablaId));
        }

        [Fact]
        public void PendienteInferiorAlMinimo()
        {
            var tramos = new List<TramoHs5> { T(1, 10, null, 1.02, null, 1.0, 5) }; // 0,4 %
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 1 }, out _).Single();
            Assert.True(ev.PendienteInsuficiente);
        }
    }
}
