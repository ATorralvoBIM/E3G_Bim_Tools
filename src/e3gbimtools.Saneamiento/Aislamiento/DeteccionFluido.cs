// ============================================================================
//  DeteccionFluido.cs
//  Decide si un sistema de tuberias que no es de fontaneria (calefaccion,
//  refrigeracion, lineas frigorificas, otros) lleva fluido caliente, frio o
//  refrigerante: primero por el nombre del tipo de sistema y, si no lo aclara,
//  por su temperatura de fluido.
//  Sin dependencias de Revit: lo enlaza el proyecto de tests.
// ============================================================================
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace E3G.Saneamiento
{
    /// <summary>Resultado de la deteccion: fluido, temperatura propuesta y si es fiable.</summary>
    public sealed class FluidoDetectado
    {
        public FluidoRite Fluido { get; }
        public double Temperatura { get; }

        /// <summary>false si no se ha podido saber: la fila sale desmarcada y con «Revisar».</summary>
        public bool Seguro { get; }

        public FluidoDetectado(FluidoRite fluido, double temperatura, bool seguro)
        {
            Fluido = fluido;
            Temperatura = temperatura;
            Seguro = seguro;
        }

        public string Etiqueta
            => !Seguro ? "Revisar"
               : Fluido == FluidoRite.Refrigerante ? "Frigorífica"
               : Fluido == FluidoRite.Caliente || Fluido == FluidoRite.Acs ? "Caliente" : "Frío";

        public static FluidoDetectado Por(FluidoRite fluido)
            => new FluidoDetectado(fluido, RiteAislamiento.TemperaturaPorDefecto(fluido), true);
    }

    public static class DeteccionFluido
    {
        /// <summary>RITE IT 1.2.4.2.1.1: se aislan los fluidos calientes de mas de 40 °C.</summary>
        public const double MinimoCaliente = 40;

        /// <summary>
        /// Agua enfriada: 7/12 °C. Por encima suele ser el valor por defecto de Revit
        /// (15,6 °C), que no dice nada del sistema.
        /// </summary>
        public const double MaximoFrio = 14;

        /// <summary>Con el nombre caliente, por debajo de esto la temperatura guardada no es creible.</summary>
        private const double MinimoCreibleCaliente = 25;

        // Raices (se buscan dentro del nombre, sin tildes y en mayusculas).
        private static readonly string[] RaicesCalientes =
            { "CALEF", "CALENT", "CALIENT", "CALOR", "RADIADOR", "RADIANT", "SOLAR", "HEATING" };
        private static readonly string[] RaicesFrias =
            { "FRIO", "FRIA", "FRED", "REFRIG", "HELAD", "GELAD", "ENFRIA", "CHILL", "COOLING" };

        // Siglas y palabras cortas: solo como palabra entera.
        private static readonly string[] PalabrasCalientes = { "ACS", "CAL", "HW", "HOT" };
        private static readonly string[] PalabrasFrias = { "CHW", "COLD" };

        // Equipos cuyo nombre lleva «calor» pero cuyo circuito puede ser frio o caliente.
        private static readonly string[] Neutros = { "BOMBA DE CALOR", "BOMBA CALOR", "RECUPERADOR DE CALOR" };

        // Lineas frigorificas de expansion directa. Van antes que «REFRIG», que en
        // «Refrigeración» es agua enfriada: «REFRIGERANT» no casa con «REFRIGERACION».
        private static readonly string[] RaicesRefrigerante =
            { "FRIGORIF", "REFRIGERANT", "EXPANSION DIRECTA", "FREON" };
        private static readonly string[] PalabrasRefrigerante = { "VRV", "VRF", "SPLIT", "MULTISPLIT" };

        /// <summary>Codigo de gas refrigerante: R22, R32, R410A, R-454B, R1234YF…</summary>
        private static readonly Regex CodigoRefrigerante =
            new Regex(@"(^|[^A-Z0-9])R[- ]?\d{2,4}[A-Z]{0,2}([^A-Z0-9]|$)", RegexOptions.Compiled);

        /// <param name="nombre">Nombre del tipo de sistema de tuberias.</param>
        /// <param name="temperaturaSistema">Temperatura de fluido del tipo de sistema en °C, o null.</param>
        public static FluidoDetectado Detectar(string nombre, double? temperaturaSistema)
        {
            double? t = temperaturaSistema.HasValue ? Math.Round(temperaturaSistema.Value, 1) : (double?)null;

            switch (PorNombre(nombre))
            {
                case FluidoRite.Refrigerante:
                    return new FluidoDetectado(FluidoRite.Refrigerante,
                        t <= MaximoFrio ? t.Value : RiteAislamiento.TemperaturaPorDefecto(FluidoRite.Refrigerante), true);
                case FluidoRite.Caliente:
                    return new FluidoDetectado(FluidoRite.Caliente,
                        t >= MinimoCreibleCaliente ? t.Value : RiteAislamiento.TemperaturaPorDefecto(FluidoRite.Caliente), true);
                case FluidoRite.Frio:
                    return new FluidoDetectado(FluidoRite.Frio,
                        t <= MaximoFrio ? t.Value : RiteAislamiento.TemperaturaPorDefecto(FluidoRite.Frio), true);
            }

            if (t >= MinimoCaliente) return new FluidoDetectado(FluidoRite.Caliente, t.Value, true);
            if (t <= MaximoFrio) return new FluidoDetectado(FluidoRite.Frio, t.Value, true);
            return new FluidoDetectado(FluidoRite.Frio, RiteAislamiento.TemperaturaPorDefecto(FluidoRite.Frio), false);
        }

        /// <summary>
        /// Refrigerante (linea frigorifica), Caliente, Frio, o null si el nombre no dice
        /// nada o dice las dos cosas («frío/calor»).
        /// </summary>
        public static FluidoRite? PorNombre(string nombre)
        {
            string n = Normalizar(nombre);
            var palabras = Regex.Split(n, "[^A-Z0-9]+");
            if (RaicesRefrigerante.Any(n.Contains) || PalabrasRefrigerante.Any(palabras.Contains) ||
                CodigoRefrigerante.IsMatch(n))
                return FluidoRite.Refrigerante;

            foreach (var neutro in Neutros) n = n.Replace(neutro, " ");
            palabras = Regex.Split(n, "[^A-Z0-9]+");

            bool caliente = RaicesCalientes.Any(n.Contains) || PalabrasCalientes.Any(palabras.Contains);
            bool frio = RaicesFrias.Any(n.Contains) || PalabrasFrias.Any(palabras.Contains);
            if (caliente == frio) return null;
            return caliente ? FluidoRite.Caliente : FluidoRite.Frio;
        }

        private static string Normalizar(string texto)
        {
            var descompuesto = (texto ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().ToUpperInvariant();
        }
    }
}
