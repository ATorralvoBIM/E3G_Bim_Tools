using Xunit;

namespace E3G.Saneamiento.Hs5.Tests
{
    public class DeteccionFluidoTests
    {
        [Theory]
        [InlineData("Calefacción impulsión", FluidoRite.Caliente)]
        [InlineData("Retorno calefacció", FluidoRite.Caliente)]
        [InlineData("Primario ACS", FluidoRite.Caliente)]
        [InlineData("Solar térmica", FluidoRite.Caliente)]
        [InlineData("Suelo radiante", FluidoRite.Caliente)]
        [InlineData("Agua fría climatización", FluidoRite.Frio)]
        [InlineData("Refrigeración retorno", FluidoRite.Frio)]
        [InlineData("Agua helada", FluidoRite.Frio)]
        [InlineData("Aigua freda clima", FluidoRite.Frio)]
        [InlineData("CHW supply", FluidoRite.Frio)]
        // Lineas frigorificas: antes que «refrig» (agua enfriada) y que «caliente».
        [InlineData("Frigorífica gas", FluidoRite.Refrigerante)]
        [InlineData("Línia frigorífica líquid", FluidoRite.Refrigerante)]
        [InlineData("Refrigerante R-410A", FluidoRite.Refrigerante)]
        [InlineData("R32 líquido", FluidoRite.Refrigerante)]
        [InlineData("Gas caliente R454B", FluidoRite.Refrigerante)]
        [InlineData("VRV planta 1", FluidoRite.Refrigerante)]
        [InlineData("Split cocina", FluidoRite.Refrigerante)]
        public void PorNombre_ReconoceCalienteYFrio(string nombre, FluidoRite esperado)
            => Assert.Equal(esperado, DeteccionFluido.PorNombre(nombre));

        [Theory]
        [InlineData("Frío/Calor 2 tubos")]          // cambio de estacion: las dos cosas
        [InlineData("Impulsión bomba de calor")]    // el equipo no dice el fluido
        [InlineData("Condensados")]
        [InlineData("Circuito 1")]
        [InlineData("Placas")]                      // «ACS» solo como palabra entera
        [InlineData(null)]
        public void PorNombre_SinDatosOAmbiguo_DevuelveNull(string nombre)
            => Assert.Null(DeteccionFluido.PorNombre(nombre));

        [Theory]
        // El nombre manda; la temperatura del sistema solo se usa si es coherente.
        [InlineData("Calefacción", null, FluidoRite.Caliente, 60)]
        [InlineData("Calefacción", 70.0, FluidoRite.Caliente, 70)]
        [InlineData("Calefacción", 15.56, FluidoRite.Caliente, 60)]   // valor por defecto de Revit
        [InlineData("Refrigeración", 7.0, FluidoRite.Frio, 7)]
        [InlineData("Refrigeración", 15.56, FluidoRite.Frio, 10)]
        // Sin pistas en el nombre: decide la temperatura.
        [InlineData("Circuito 1", 80.0, FluidoRite.Caliente, 80)]
        [InlineData("Circuito 1", 6.98, FluidoRite.Frio, 7)]
        // Frigorificas: la temperatura es la de evaporacion (5 °C si el sistema no la trae).
        [InlineData("Frigorífica gas", null, FluidoRite.Refrigerante, 5)]
        [InlineData("VRV", 2.0, FluidoRite.Refrigerante, 2)]
        [InlineData("Refrigerante", 15.56, FluidoRite.Refrigerante, 5)]
        public void Detectar_Seguro(string nombre, double? t, FluidoRite fluido, double temperatura)
        {
            var d = DeteccionFluido.Detectar(nombre, t);
            Assert.True(d.Seguro);
            Assert.Equal(fluido, d.Fluido);
            Assert.Equal(temperatura, d.Temperatura, 1);
        }

        [Theory]
        [InlineData("Circuito 1", null)]
        [InlineData("Circuito 1", 15.56)]
        [InlineData("Circuito 1", 30.0)]
        public void Detectar_SinDatos_PideRevisar(string nombre, double? t)
        {
            var d = DeteccionFluido.Detectar(nombre, t);
            Assert.False(d.Seguro);
            Assert.Equal("Revisar", d.Etiqueta);
        }

        [Fact]
        public void Etiqueta_DeFontaneria()
        {
            Assert.Equal("Caliente", FluidoDetectado.Por(FluidoRite.Acs).Etiqueta);
            Assert.Equal("Frío", FluidoDetectado.Por(FluidoRite.Afs).Etiqueta);
            Assert.Equal("Frigorífica", FluidoDetectado.Por(FluidoRite.Refrigerante).Etiqueta);
        }
    }
}
