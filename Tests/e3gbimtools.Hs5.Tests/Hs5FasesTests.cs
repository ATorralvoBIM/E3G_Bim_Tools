using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace E3G.Saneamiento.Hs5.Tests
{
    public class Hs5AparatosTests
    {
        [Theory]
        [InlineData("MC_E3G_INODOR_SA", "Estándar", "Inodoro · Con cisterna")]
        [InlineData("Inodoro suspendido", "Fluxómetro", "Inodoro · Con fluxómetro")]
        [InlineData("MC_E3G_DUTXA_SA", "80x80", "Ducha")]
        [InlineData("MC_E3G_LAVABO_SA", "", "Lavabo")]
        [InlineData("MC_E3G_AIGÜERA_SA", "", "Fregadero · De cocina")]
        [InlineData("Fregadero restaurante", "", "Fregadero · De laboratorio, restaurante, etc.")]
        [InlineData("MC_E3G_RENTAVAIXELLES_SA", "", "Lavavajillas")]
        [InlineData("MC_E3G_RENTADORA_SA", "", "Lavadora")]
        [InlineData("Bañera acrílica", "", "Bañera (con o sin ducha)")]
        [InlineData("Bidé", "", "Bidé")]
        [InlineData("Urinario", "Suspendido", "Urinario · Suspendido")]
        [InlineData("Sumidero sifónico", "", "Sumidero sifónico")]
        [InlineData("MC_E3G_TERMO_SA", "", MapeoHs5.Otro)]
        [InlineData("Mueble", "", null)]
        public void Detectar(string familia, string tipo, string esperado)
        {
            Assert.Equal(esperado, Hs5Aparatos.Detectar(familia, tipo));
        }

        [Fact]
        public void Resolver_InodoroPrivado()
        {
            var a = Hs5Aparatos.Resolver(1, "MC_E3G_INODOR_SA", "", null, usoPublico: false, dnDesagueMm: 110);
            Assert.Equal(4, a.UD);
            Assert.Equal(110, a.DN); // ø100 de la norma → ø110 comercial
            Assert.False(a.SinMapear);
        }

        [Fact]
        public void Resolver_UrinarioEnUsoPrivadoTomaElPublico()
        {
            var a = Hs5Aparatos.Resolver(1, "Urinario", "Pedestal", null, false, 50);
            Assert.Equal(4, a.UD);
            Assert.Equal(50, a.DN);
            Assert.NotNull(a.Aviso);
        }

        [Fact]
        public void Resolver_MapeoManualPrevaleceYOtroUsaTabla42()
        {
            var a = Hs5Aparatos.Resolver(1, "Mueble", "", MapeoHs5.Otro, false, 50);
            Assert.Equal(3, a.UD); // tabla 4.2: ø50 → 3 UD
            Assert.Equal(50, a.DN);
            Assert.False(a.SinMapear);

            var b = Hs5Aparatos.Resolver(1, "MC_E3G_INODOR_SA", "", "Lavabo", true, 110);
            Assert.Equal(2, b.UD);
            Assert.Equal(40, b.DN);
        }

        [Fact]
        public void Resolver_UDDeLaFamiliaDecideElUso()
        {
            // Lavabo con 2 UD en la familia → uso público → Ø40 aunque el proyecto esté en privado.
            var a = Hs5Aparatos.Resolver(1, "MC_E3G_LAVABO_SA", "", null, usoPublico: false, dnDesagueMm: 40, udAparato: 2);
            Assert.True(a.UsoPublico);
            Assert.Equal(2, a.UD);
            Assert.Equal(40, a.DN);
            Assert.Null(a.Aviso);

            // UD que no está en la tabla: se respeta la UD, Ø del uso elegido y aviso.
            var b = Hs5Aparatos.Resolver(1, "MC_E3G_LAVABO_SA", "", null, false, 40, udAparato: 1.5);
            Assert.Equal(1.5, b.UD);
            Assert.Equal(32, b.DN);
            Assert.NotNull(b.Aviso);
        }

        [Fact]
        public void Resolver_OtroNoCompruebaUD()
        {
            var a = Hs5Aparatos.Resolver(1, "MC_E3G_TERMO_SA", "", null, false, 32);
            Assert.False(a.ComprobarUD);
            Assert.Equal(32, a.DN);
        }

        [Fact]
        public void Config_SinRegimenDelProyectoFactorUno()
        {
            var cfg = new Hs5Config();
            Assert.False(cfg.IntensidadDefinida);
            Assert.Equal(1.0, cfg.FactorPluviometrico, 6);
            Assert.Contains("f = 1", cfg.DescripcionPluviometria);

            // Proyecto en zona A, isoyeta 50 → i = 155 mm/h (tabla B.1).
            cfg.ZonaProyecto = "A"; cfg.IsoyetaProyecto = 50;
            cfg.IntensidadProyecto = Hs5Tablas.Intensidad('A', 50);
            Assert.Equal(1.55, cfg.FactorPluviometrico, 6);
            Assert.Equal("zona A · isoyeta 50 → i = 155 mm/h · f = 1,55", cfg.DescripcionPluviometria);
        }

        [Fact]
        public void Resolver_SinMapearEIgnorado()
        {
            Assert.True(Hs5Aparatos.Resolver(1, "Mueble", "", null, false, 40).SinMapear);
            Assert.True(Hs5Aparatos.Resolver(1, "Mueble", "", MapeoHs5.Ignorar, false, 40).Ignorado);
        }
    }

    public class Hs5TablasPluvialesYVentilacionTests
    {
        [Fact]
        public void AnejoB_Intensidad()
        {
            Assert.Equal(90, Hs5Tablas.Intensidad('A', 30));
            Assert.Equal(90, Hs5Tablas.Intensidad('B', 35)); // isoyeta intermedia → superior (40)
            Assert.Equal(365, Hs5Tablas.Intensidad('A', 120));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(100, 90)]
        [InlineData(250, 90)]
        [InlineData(300, 108)]
        public void SuperficieEquivalente(double ud, double m2) =>
            Assert.Equal(m2, Hs5Tablas.SuperficieEquivalente(ud), 6);

        [Theory]
        [InlineData(50, 2)]
        [InlineData(150, 3)]
        [InlineData(499, 4)]
        [InlineData(600, 4)]
        [InlineData(1000, 7)]
        public void NumeroSumideros(double s, int n) => Assert.Equal(n, Hs5Tablas.NumeroSumideros(s));

        [Fact]
        public void T4_8_y_T4_9()
        {
            Assert.Equal(63, Hs5Tablas.T4_8.Consultar(90, 0).DNMinimo);
            Assert.Equal(110, Hs5Tablas.T4_9.Consultar(180, 0).DNMinimo);
            Assert.Equal(315, Hs5Tablas.T4_9.Consultar(4589, 1).DNMinimo);
            Assert.True(Hs5Tablas.T4_8.Consultar(2701, 0).FueraDeTabla);
        }

        [Fact]
        public void T4_10_VentilacionSecundaria()
        {
            // Bajante ø110, 300 UD (fila 360), L = 50 m → ø63 admite 10 m, ø65 admite 51 m.
            Assert.Equal(65, Hs5Tablas.VentilacionSecundaria(110, 300, 50).DNMinimo);
            // L = 100 m → ø80 (79 m) no basta, columna ø100 de la norma (270 m) → ø110 comercial.
            Assert.Equal(110, Hs5Tablas.VentilacionSecundaria(110, 300, 100).DNMinimo);
            // UD por encima de la última fila del grupo → fuera de tabla.
            Assert.True(Hs5Tablas.VentilacionSecundaria(110, 800, 5).FueraDeTabla);
        }

        [Fact]
        public void T4_11_y_T4_12()
        {
            Assert.Equal(63, Hs5Tablas.VentilacionCadaPlanta(110));
            Assert.Equal(90, Hs5Tablas.VentilacionCadaPlanta(160));
            Assert.Equal(40, Hs5Tablas.VentilacionTerciaria(100, 1, 100).DNMinimo); // 32: 40 m, 40: 110 m
            Assert.Equal(50, Hs5Tablas.VentilacionTerciaria(100, 2, 100).DNMinimo); // al 2 %: 40 → 44 m
            Assert.Equal(32, Hs5Tablas.VentilacionTerciaria(50, 1, 1000).DNMinimo);  // > 300 m
        }

        [Fact]
        public void Dn110ComercialEntraPorLaFilaDe100()
        {
            Assert.Equal((40, 40, false), Hs5Tablas.Arqueta(110));       // fila ø100 de la 4.13
            Assert.Equal(40, Hs5Tablas.VentilacionTerciaria(110, 1, 100).DNMinimo); // grupo ø100 de la 4.12
            Assert.Equal(6, Hs5Tablas.UDPorDiametroDesague(110));         // fila ø100 de la 4.2
            Assert.DoesNotContain(Hs5Tablas.T4_1, a => a.DNPrivado == 100 || a.DNPublico == 100);
        }

        [Fact]
        public void T4_13_Arquetas()
        {
            Assert.Equal((60, 60, false), Hs5Tablas.Arqueta(200));
            Assert.Equal((60, 70, false), Hs5Tablas.Arqueta(220));
            Assert.Equal((40, 40, false), Hs5Tablas.Arqueta(90));
        }
    }

    public class Hs5RedCompletaTests
    {
        private static readonly double[] Niveles = { 0, 3, 6, 9, 12 };

        private static TramoHs5 T(long id, double ud, string j0, double z0, string j1, double z1, double lh,
                                  int dn = 110, RedHs5Tipo red = RedHs5Tipo.Residual, double sup = 0) =>
            new TramoHs5
            {
                Id = id, UD = ud, Junta0 = j0, Z0 = z0, Junta1 = j1, Z1 = z1, LongHorizontal = lh,
                DNActual = dn, Red = red, Superficie = sup,
            };

        private static AparatoConectado Inodoro(long id) =>
            Hs5Aparatos.Resolver(id, "MC_E3G_INODOR_SA", "", null, false, 110);
        private static AparatoConectado Lavabo(long id) =>
            Hs5Aparatos.Resolver(id, "MC_E3G_LAVABO_SA", "", null, false, 32);

        /// <summary>
        ///  WC ─A─ J1 ─B─ T ─D─▶ (colector)
        ///                │
        ///  LAV ──────C───┘
        /// </summary>
        private static (List<TramoHs5> Tramos, Dictionary<string, List<AparatoConectado>> Aparatos) RedAparatos() =>
            (new List<TramoHs5>
            {
                T(1, 3, "WC", 1.20, "J1", 1.19, 0.5, dn: 90),   // A: ø90 < ø110 y 3 UD < 4
                T(2, 4, "J1", 1.19, "T", 1.18, 0.5, dn: 110),   // B: sigue siendo derivación del inodoro
                T(3, 1, "LAV", 1.25, "T", 1.18, 1.0, dn: 40),   // C: derivación del lavabo
                T(4, 5, "T", 1.18, "Z", 1.13, 5.0, dn: 90),     // D: ramal tras la unión, al 1 %
            },
            new Dictionary<string, List<AparatoConectado>>
            {
                ["WC"] = new List<AparatoConectado> { Inodoro(100) },
                ["LAV"] = new List<AparatoConectado> { Lavabo(101) },
            });

        [Fact]
        public void Derivacion_DelAparatoHastaLaPrimeraUnion()
        {
            var (tramos, aparatos) = RedAparatos();
            var red = Hs5Topologia.Clasificar(tramos, Niveles, aparatos.Keys.ToList());
            Assert.Equal(TipoTramo.Derivacion, red.Tramos[1].Tipo);
            Assert.Equal(TipoTramo.Derivacion, red.Tramos[2].Tipo); // tras el codo J1, aún sin acometidas
            Assert.Equal(red.Tramos[1].Derivacion, red.Tramos[2].Derivacion);
            Assert.Equal(TipoTramo.Derivacion, red.Tramos[3].Tipo);
            Assert.Equal(TipoTramo.Ramal, red.Tramos[4].Tipo);       // después de la unión T
        }

        [Fact]
        public void Derivacion_DiametroYUDDelAparato()
        {
            var (tramos, aparatos) = RedAparatos();
            var red = Hs5Topologia.Clasificar(tramos, Niveles, aparatos.Keys.ToList());
            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), tramos.Select(t => t.Id).ToList(), out _, aparatos)
                .ToDictionary(e => e.Id);

            Assert.Equal("4.1", ev[1].TablaId);
            Assert.Equal(110, ev[1].DNMinimo);
            Assert.True(ev[1].UDInsuficiente);
            Assert.False(ev[1].EsCorrecto);

            Assert.Equal(110, ev[2].DNMinimo);
            Assert.True(ev[2].EsCorrecto);       // ø110 ≥ ø110 y 4 UD

            Assert.Equal(32, ev[3].DNMinimo);    // lavabo privado
            Assert.True(ev[3].EsCorrecto);
        }

        [Fact]
        public void NoDisminuirAguasAbajo_PropagaElDNDelInodoro()
        {
            var (tramos, aparatos) = RedAparatos();
            var red = Hs5Topologia.Clasificar(tramos, Niveles, aparatos.Keys.ToList());
            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 4 }, out _, aparatos).Single();
            // Por la 4.3 al 1 % bastaría ø90, pero aguas arriba se exige ø110.
            Assert.Equal(110, ev.DNMinimo);
            Assert.True(ev.ReduccionAguasAbajo); // ø90 tras un ø110 real
            Assert.False(ev.EsCorrecto);

            var sinRegla = new Hs5Config { NoDisminuirAguasAbajo = false };
            var ev2 = Hs5Evaluador.Evaluar(tramos, red, sinRegla, new long[] { 4 }, out _, aparatos).Single();
            Assert.Equal(90, ev2.DNMinimo);
        }

        [Fact]
        public void AparatoIgnorado_SeCompruebaComoRamal()
        {
            var (tramos, aparatos) = RedAparatos();
            aparatos["WC"] = new List<AparatoConectado> { Hs5Aparatos.Resolver(100, "X", "", MapeoHs5.Ignorar, false, 0) };
            var red = Hs5Topologia.Clasificar(tramos, Niveles, aparatos.Keys.ToList());
            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 1 }, out _, aparatos).Single();
            Assert.Equal(TipoTramo.Ramal, ev.Tipo);
            Assert.Equal("4.3", ev.TablaId);
        }

        [Fact]
        public void Pluviales_BajanteYColectorConFactor()
        {
            var tramos = new List<TramoHs5>
            {
                T(1, 0, null, 9, "B", 0, 0, red: RedHs5Tipo.Pluvial, sup: 100),     // bajante: 90 m² → ø63
                T(2, 0, "B", 0, "C", -0.05, 5, red: RedHs5Tipo.Pluvial, sup: 200),  // colector 1 %: 180 m² → ø110
            };
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var cfgSin = new Hs5Config { NoDisminuirAguasAbajo = false, IntensidadProyecto = 90 }; // f = 0,9
            var ev = Hs5Evaluador.Evaluar(tramos, red, cfgSin, new long[] { 1, 2 }, out _).ToDictionary(e => e.Id);
            Assert.Equal("4.8", ev[1].TablaId);
            Assert.Equal(63, ev[1].DNMinimo);
            Assert.Equal("4.9", ev[2].TablaId);
            Assert.Equal(110, ev[2].DNMinimo);
        }

        [Fact]
        public void Pluviales_SinRegimenNoReduceDiametro()
        {
            // 113,9 m² sin régimen definido (f = 1) supera los 113 m² de ø63 → ø75.
            var tramos = new List<TramoHs5> { T(1, 0, null, 9, "B", 0, 0, red: RedHs5Tipo.Pluvial, sup: 113.9) };
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 1 }, out _).Single();
            Assert.Equal(75, ev.DNMinimo);
        }

        [Fact]
        public void Mixto_SumaSuperficieEquivalente()
        {
            var cfg = new Hs5Config { IntensidadProyecto = 90, NoDisminuirAguasAbajo = false }; // f = 0,9
            // 300 UD → 108 m²; (100 + 108) × 0,9 = 187,2 m² → 4.9 al 1 %: ø110.
            var tramos = new List<TramoHs5> { T(1, 300, "A", 0, "B", -0.05, 5, red: RedHs5Tipo.Mixta, sup: 100) };
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var ev = Hs5Evaluador.Evaluar(tramos, red, cfg, new long[] { 1 }, out _).Single();
            Assert.Equal("4.9", ev.TablaId);
            Assert.Equal(110, ev.DNMinimo);
        }

        /// <summary>Bajante de 4 tramos (T3..T0) con ramales y ventilación.</summary>
        private static List<TramoHs5> RedVentilada(bool conSecundaria)
        {
            var l = new List<TramoHs5>
            {
                T(1, 0,  "TOP", 9.5, "T3", 8.7, 0),
                T(2, 20, "T3", 8.7,  "T2", 5.7, 0),
                T(3, 40, "T2", 5.7,  "T1", 2.7, 0),
                T(4, 70, "T1", 2.7,  "T0", -0.3, 0),
                T(10, 20, null, 8.75, "T3", 8.7, 5, dn: 90),
                T(11, 20, null, 5.75, "T2", 5.7, 5, dn: 90),
                T(12, 30, null, 2.75, "T1", 2.7, 5, dn: 90),
                T(50, 0, "TOP", 9.5, null, 10.5, 0, dn: 110, red: RedHs5Tipo.Ventilacion), // primaria
            };
            if (conSecundaria)
            {
                // Columna paralela unida a la bajante en T3 y T1 por dos tubos de unión.
                l.Add(T(60, 0, "T3", 8.7, "S3", 8.9, 0.3, dn: 75, red: RedHs5Tipo.Ventilacion));
                l.Add(T(61, 0, "S3", 8.9, "S1", 2.9, 0, dn: 75, red: RedHs5Tipo.Ventilacion));
                l.Add(T(62, 0, "T1", 2.7, "S1", 2.9, 0.3, dn: 75, red: RedHs5Tipo.Ventilacion));
            }
            return l;
        }

        [Fact]
        public void Ventilacion_PrimariaMismoDNQueLaBajante()
        {
            var tramos = RedVentilada(false);
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            Assert.Equal(TipoTramo.VentPrimaria, red.Tramos[50].Tipo);
            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 50 }, out _).Single();
            Assert.Equal(110, ev.DNMinimo); // DN real de la bajante (ø110) ≥ exigido (ø90)
            Assert.True(ev.EsCorrecto);
        }

        [Fact]
        public void Ventilacion_Secundaria()
        {
            var tramos = RedVentilada(true);
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            Assert.Equal(TipoTramo.VentSecundaria, red.Tramos[61].Tipo);
            var v = red.Ventilaciones[red.Tramos[61].Ventilacion];
            Assert.Equal(2, v.UnionesBajante);

            var ev = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 61 }, out _).Single();
            // 2 uniones < 3 plantas → tabla 4.10: bajante ø110, 70 UD (fila 180), L ≈ 6,6 m → ø63 (15 m);
            // pero ≥ ½ ø110 = 55 → ø63 ya lo cumple.
            Assert.Equal("4.10", ev.TablaId);
            Assert.Equal(63, ev.DNMinimo);
            Assert.True(ev.EsCorrecto); // ø75
        }

        [Fact]
        public void Arqueta_DimensionesSegunColectorDeSalida()
        {
            var tramos = new List<TramoHs5>
            {
                T(1, 70, "A", 0, "ARQ", -0.05, 5, dn: 200),
                T(2, 70, "ARQ", -0.10, "Z", -0.15, 5, dn: 200),
            };
            var red = Hs5Topologia.Clasificar(tramos, Niveles);
            var arquetas = new List<ArquetaHs5> { new ArquetaHs5 { Id = 900, Junta = "ARQ", LargoCm = 50, AnchoCm = 50 } };
            var evs = Hs5Evaluador.Evaluar(tramos, red, new Hs5Config(), new long[] { 1 }, out _, null, arquetas);
            var arq = evs.Single(e => e.Tipo == TipoTramo.Arqueta);
            Assert.Equal("≥ 60×60 cm", arq.TextoExigido);
            Assert.True(arq.DimensionInsuficiente);
            Assert.False(arq.EsCorrecto);
        }
    }
}
