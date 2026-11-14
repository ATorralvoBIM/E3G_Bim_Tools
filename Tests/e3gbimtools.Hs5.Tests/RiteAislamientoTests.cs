using Xunit;

namespace E3G.Saneamiento.Hs5.Tests
{
    public class RiteAislamientoTests
    {
        [Theory]
        // Caliente interior (1.2.4.2.1): limites de columna y de fila.
        [InlineData(FluidoRite.Caliente, UbicacionRite.Interior, 40, 35, 25)]
        [InlineData(FluidoRite.Caliente, UbicacionRite.Interior, 60, 35.0000001, 25)]
        [InlineData(FluidoRite.Caliente, UbicacionRite.Interior, 60.5, 100, 40)]
        [InlineData(FluidoRite.Caliente, UbicacionRite.Interior, 180, 200, 50)]
        // Caliente exterior (1.2.4.2.2).
        [InlineData(FluidoRite.Caliente, UbicacionRite.Exterior, 50, 150, 45)]
        // Frio interior (1.2.4.2.3).
        [InlineData(FluidoRite.Frio, UbicacionRite.Interior, 10, 32, 25)]
        [InlineData(FluidoRite.Frio, UbicacionRite.Interior, 10.1, 32, 20)]
        [InlineData(FluidoRite.Frio, UbicacionRite.Interior, 0, 75, 40)]
        // Frio exterior (1.2.4.2.4).
        [InlineData(FluidoRite.Frio, UbicacionRite.Exterior, 5, 32, 45)]
        [InlineData(FluidoRite.Frio, UbicacionRite.Exterior, -5, 120, 70)]
        // ACS (1.2.4.2): no depende de la temperatura.
        [InlineData(FluidoRite.Acs, UbicacionRite.Interior, 60, 32, 30)]
        [InlineData(FluidoRite.Acs, UbicacionRite.Interior, 20, 110, 45)]
        [InlineData(FluidoRite.Acs, UbicacionRite.Exterior, 60, 75, 45)]
        public void Espesor_DevuelveElValorDeLaTabla(
            FluidoRite fluido, UbicacionRite ubicacion, double t, double d, int esperado)
            => Assert.Equal(esperado, RiteAislamiento.Espesor(fluido, ubicacion, t, d));

        // ── Agua fria sanitaria: 9 mm comprobados contra la condensacion (UNE-EN ISO 12241) ──

        [Theory]
        [InlineData(25, 70, 19.15)]
        [InlineData(20, 50, 9.26)]
        [InlineData(20, 100, 20)]
        public void PuntoRocio_Magnus(double t, double hr, double esperado)
            => Assert.InRange(RiteAislamiento.PuntoRocio(t, hr), esperado - 0.05, esperado + 0.05);

        [Theory]
        [InlineData(UbicacionRite.Interior, 16)]
        [InlineData(UbicacionRite.Interior, 25)]
        [InlineData(UbicacionRite.Interior, 63)]
        [InlineData(UbicacionRite.Interior, 110)]
        [InlineData(UbicacionRite.Exterior, 160)]
        [InlineData(UbicacionRite.Interior, 200)]
        public void AguaFria_ConAirePorDefecto_NueveMmEnTodosLosDiametros(UbicacionRite ubicacion, double d)
            => Assert.Equal(9, RiteAislamiento.Espesor(FluidoRite.Afs, ubicacion, 10, d));

        [Theory]
        [InlineData(10, 25, 75, 25, 9)]
        [InlineData(10, 25, 75, 160, 13)]
        [InlineData(10, 25, 80, 16, 13)]
        [InlineData(10, 25, 80, 110, 19)]
        [InlineData(5, 25, 70, 16, 9)]
        [InlineData(5, 25, 70, 110, 13)]
        [InlineData(20, 25, 70, 110, 9)]   // agua por encima del rocio: no condensa
        public void AguaFria_SubeAlEspesorComercialQueEvitaLaCondensacion(
            double agua, double aire, double hr, double d, int esperado)
            => Assert.Equal(esperado, RiteAislamiento.EspesorAguaFria(agua, d, new AmbienteCondensacion(aire, hr)));

        [Fact]
        public void AguaFria_SinEspesorComercialSuficiente_DevuelveNull()
            => Assert.Null(RiteAislamiento.EspesorAguaFria(10, 110, new AmbienteCondensacion(25, 99)));

        [Fact]
        public void AguaFria_ConNueveMm_LaSuperficieQuedaSobreElRocio()
        {
            var aire = AmbienteCondensacion.PorDefecto;
            foreach (double d in RiteAislamiento.DiametrosAguaFria)
                Assert.True(RiteAislamiento.TemperaturaSuperficial(d, 9, 10, aire.Temperatura) > aire.PuntoRocio + 0.5);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(40)]
        public void AguaFria_TemperaturaFueraDeRango_DevuelveNull(double t)
            => Assert.Null(RiteAislamiento.Espesor(FluidoRite.Afs, UbicacionRite.Interior, t, 32));

        [Fact]
        public void TablaAguaFria_NueveMmHastaSetentaPorCiento()
        {
            var tabla = RiteAislamiento.TablaAguaFria(10, 25);
            int col70 = System.Array.IndexOf(RiteAislamiento.HumedadesAguaFria, 70.0);
            for (int f = 0; f < tabla.Filas.Length; f++)
                Assert.Equal(9, tabla.Valores[f, col70]);
            Assert.Equal(13, tabla.Valores[0, tabla.Columnas.Length - 1]);   // D 16 al 80 %
        }

        // ── Lineas frigorificas reversibles: la mas exigente entre frio y gas caliente (ap. 4) ──

        [Theory]
        [InlineData(UbicacionRite.Interior, 5, 15.88, 25)]    // frio 25 = caliente 25
        [InlineData(UbicacionRite.Exterior, 5, 22.22, 45)]    // manda el frio (45 > 35)
        [InlineData(UbicacionRite.Interior, 12, 28.58, 25)]   // manda el gas caliente (25 > 20)
        [InlineData(UbicacionRite.Interior, 5, 41.28, 30)]    // 35 < D ≤ 60
        [InlineData(UbicacionRite.Interior, -5, 15.88, 30)]   // evaporacion bajo cero
        public void Refrigerante_TomaLaCondicionMasExigente(UbicacionRite ubicacion, double t, double d, int esperado)
            => Assert.Equal(esperado, RiteAislamiento.Espesor(FluidoRite.Refrigerante, ubicacion, t, d));

        [Fact]
        public void Refrigerante_EvaporacionFueraDeTabla_DevuelveNull()
            => Assert.Null(RiteAislamiento.Espesor(FluidoRite.Refrigerante, UbicacionRite.Interior, -10, 15.88));

        [Theory]
        [InlineData(FluidoRite.Caliente, 39.9)]
        [InlineData(FluidoRite.Caliente, 180.1)]
        [InlineData(FluidoRite.Frio, -10)]
        public void Espesor_FueraDeTabla_DevuelveNull(FluidoRite fluido, double t)
            => Assert.Null(RiteAislamiento.Espesor(fluido, UbicacionRite.Interior, t, 32));

        [Theory]
        [InlineData(UbicacionRite.Interior)]
        [InlineData(UbicacionRite.Exterior)]
        public void TablaAcs_EsLaColumnaDe60a100MasCincoMm(UbicacionRite ubicacion)
        {
            var caliente = RiteAislamiento.Tabla(FluidoRite.Caliente, ubicacion);
            var acs = RiteAislamiento.Tabla(FluidoRite.Acs, ubicacion);
            for (int fila = 0; fila < RiteAislamiento.FilasDiametro.Length; fila++)
                Assert.Equal(caliente.Valores[fila, 1] + 5, acs.Valores[fila, 0]);
        }
    }
}
