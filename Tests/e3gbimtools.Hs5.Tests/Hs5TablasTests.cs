using System;
using System.Linq;
using Xunit;

namespace E3G.Saneamiento.Hs5.Tests
{
    public class Hs5TablasTests
    {
        // ── Tabla 4.5 · colectores ──

        [Theory]
        [InlineData(96, 0, 90)]     // límite exacto de ø90 al 1 %
        [InlineData(97, 0, 110)]
        [InlineData(264, 0, 110)]
        [InlineData(8300, 0, 350)]
        [InlineData(20, 1, 50)]     // al 2 % sí se admite ø50
        [InlineData(21, 1, 63)]
        [InlineData(10000, 1, 350)]
        [InlineData(25, 2, 50)]
        [InlineData(12000, 2, 350)]
        public void T4_5_DevuelveDNMinimo(double ud, int col, int dnEsperado)
        {
            var c = Hs5Tablas.T4_5.Consultar(ud, col);
            Assert.False(c.FueraDeTabla);
            Assert.Equal(dnEsperado, c.DNMinimo);
        }

        [Fact]
        public void T4_5_Al1PorCiento_NoAdmiteMenoresDe90()
        {
            // "–" en la norma para ø50, ø63 y ø75 al 1 %: 1 UD ya exige ø90.
            Assert.Equal(90, Hs5Tablas.T4_5.Consultar(1, 0).DNMinimo);
        }

        [Fact]
        public void T4_5_FueraDeTabla()
        {
            var c = Hs5Tablas.T4_5.Consultar(8301, 0);
            Assert.True(c.FueraDeTabla);
            Assert.Equal(350, c.DNMinimo);
        }

        [Fact]
        public void T4_5_CoincideConElMotorAnteriorAl1PorCiento()
        {
            // Valores que tenía CteMotor.CteDN / CteUD (sólo 1 %).
            int[] dn = { 90, 110, 125, 160, 200, 250, 315, 350 };
            double[] ud = { 96, 264, 390, 880, 1600, 2900, 5710, 8300 };
            for (int i = 0; i < dn.Length; i++)
                Assert.Equal(dn[i], Hs5Tablas.T4_5.Consultar(ud[i], 0).DNMinimo);
        }

        // ── Tabla 4.3 · ramales ──

        [Theory]
        [InlineData(1, 1, 32)]
        [InlineData(2, 1, 40)]
        [InlineData(3, 2, 40)]
        [InlineData(11, 1, 63)]
        [InlineData(47, 0, 90)]
        [InlineData(48, 0, 110)]
        [InlineData(1680, 2, 200)]
        public void T4_3_DevuelveDNMinimo(double ud, int col, int dnEsperado)
        {
            Assert.Equal(dnEsperado, Hs5Tablas.T4_3.Consultar(ud, col).DNMinimo);
        }

        [Fact]
        public void T4_3_FueraDeTabla()
        {
            Assert.True(Hs5Tablas.T4_3.Consultar(871, 0).FueraDeTabla);
        }

        // ── Tabla 4.4 · bajantes ──

        [Theory]
        [InlineData(10, 1, false, 50)]
        [InlineData(11, 1, false, 63)]
        [InlineData(25, 5, false, 50)]   // > 3 plantas admite más UD por DN
        [InlineData(360, 2, false, 110)]
        [InlineData(740, 8, false, 110)]
        [InlineData(181, 2, true, 110)]  // criterio ramal
        [InlineData(1650, 6, true, 315)]
        public void T4_4_DevuelveDNMinimo(double ud, int plantas, bool ramal, int dnEsperado)
        {
            int col = Hs5Tablas.ColumnaBajante(plantas, ramal);
            Assert.Equal(dnEsperado, Hs5Tablas.T4_4.Consultar(ud, col).DNMinimo);
        }

        [Fact]
        public void T4_4_NoVaPorPendiente()
        {
            Assert.False(Hs5Tablas.T4_4.VaPorPendiente);
            Assert.Throws<InvalidOperationException>(() => Hs5Tablas.T4_4.ColumnaPorPendiente(2, out _));
        }

        // ── Columna por pendiente ──

        [Theory]
        [InlineData(1.0, 0, false)]
        [InlineData(0.97, 0, false)]  // tolerancia de modelado
        [InlineData(1.5, 0, false)]   // se baja a la columna inferior (conservador)
        [InlineData(2.0, 1, false)]
        [InlineData(3.9, 1, false)]
        [InlineData(4.0, 2, false)]
        [InlineData(10, 2, false)]
        [InlineData(0.5, 0, true)]    // por debajo del mínimo
        [InlineData(0, 0, true)]
        public void ColumnaPorPendiente(double pct, int colEsperada, bool insuficienteEsperado)
        {
            int col = Hs5Tablas.T4_5.ColumnaPorPendiente(pct, out bool insuficiente);
            Assert.Equal(colEsperada, col);
            Assert.Equal(insuficienteEsperado, insuficiente);
        }

        // ── Integridad de datos ──

        [Fact]
        public void Tablas_DNYLimitesCrecientes()
        {
            foreach (var t in Hs5Tablas.Verificacion)
            {
                for (int i = 1; i < t.DN.Length; i++)
                    Assert.True(t.DN[i] > t.DN[i - 1], $"Tabla {t.Id}: DN no creciente en fila {i}");

                for (int c = 0; c < t.Columnas.Length; c++)
                {
                    var limites = Enumerable.Range(0, t.DN.Length)
                        .Select(i => t.Max[i, c]).Where(v => !double.IsNaN(v)).ToList();
                    for (int i = 1; i < limites.Count; i++)
                        Assert.True(limites[i] > limites[i - 1], $"Tabla {t.Id} col {c}: límite no creciente");
                }
            }
        }

        // ── Tablas 4.1 / 4.2 ──

        [Fact]
        public void T4_1_InodoroConCisterna()
        {
            var a = Hs5Tablas.T4_1.Single(x => x.Aparato == "Inodoro" && x.Variante == "Con cisterna");
            Assert.Equal(4, a.UDPrivado);
            Assert.Equal(5, a.UDPublico);
            Assert.Equal(110, a.DNPrivado);
        }

        [Theory]
        [InlineData(32, 1)]
        [InlineData(40, 2)]
        [InlineData(45, 3)]   // fila inmediatamente superior
        [InlineData(100, 6)]
        public void T4_2_UDPorDiametroDesague(int dn, double ud)
        {
            Assert.Equal(ud, Hs5Tablas.UDPorDiametroDesague(dn));
        }

        [Fact]
        public void UDPorCaudalContinuo()
        {
            Assert.Equal(2, Hs5Tablas.UDPorCaudalContinuo(0.06), 6);
        }

        // ── Colores ──

        [Fact]
        public void Colores_TodosLosDNTienenColor()
        {
            foreach (var t in Hs5Tablas.Verificacion)
                foreach (int dn in t.DN)
                    Assert.NotEqual("—", Hs5Colores.Nombre(dn));
        }
    }
}
