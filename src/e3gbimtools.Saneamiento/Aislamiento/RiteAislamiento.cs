// ============================================================================
//  RiteAislamiento.cs
//  Espesores minimos de aislamiento de tuberias segun el RITE (RD 1027/2007),
//  IT 1.2.4.2.1.2, procedimiento simplificado. Valores contrastados con el texto
//  consolidado del BOE (BOE-A-2007-15820, actualizacion 02/08/2022).
//  Agua fria sanitaria: fuera de las tablas. El RITE (IT 1.2.4.2.1.2, ap. 9) y el
//  CTE DB HS 4 (ap. 5.1.1.3.2) obligan a evitar condensaciones; el espesor de 9 mm
//  de E3G se comprueba con el metodo de la UNE-EN ISO 12241 (temperatura
//  superficial >= temperatura de rocio).
//  Sin dependencias de Revit: lo enlaza el proyecto de tests.
// ============================================================================
using System;

namespace E3G.Saneamiento
{
    /// <summary>
    /// El orden coincide con el desplegable de la pagina de aislamiento.
    /// Refrigerante: linea frigorifica de expansion directa (split, VRV) reversible.
    /// </summary>
    public enum FluidoRite { Frio = 0, Caliente = 1, Acs = 2, Afs = 3, Refrigerante = 4 }

    public enum UbicacionRite { Interior = 0, Exterior = 1 }

    public sealed class TablaRite
    {
        public string Codigo { get; }
        public string Cabecera { get; }
        public string Titulo { get; }
        public string EncabezadoColumnas { get; }
        public string[] Columnas { get; }
        public string[] Filas { get; }

        /// <summary>[fila de diametro, columna de temperatura] en mm.</summary>
        public int[,] Valores { get; }

        public TablaRite(string codigo, string titulo, string encabezado, string[] columnas, int[,] valores,
                         string[] filas = null, string cabecera = null)
        {
            Codigo = codigo;
            Cabecera = cabecera ?? "Tabla " + codigo;
            Titulo = titulo;
            EncabezadoColumnas = encabezado;
            Columnas = columnas;
            Valores = valores;
            Filas = filas ?? RiteAislamiento.FilasDiametro;
        }
    }

    /// <summary>Aire alrededor de la tuberia, para la comprobacion de condensacion del agua fria.</summary>
    public sealed class AmbienteCondensacion
    {
        public double Temperatura { get; }
        public double HumedadRelativa { get; }

        public AmbienteCondensacion(double temperatura, double humedadRelativa)
        {
            Temperatura = temperatura;
            HumedadRelativa = humedadRelativa;
        }

        /// <summary>Interior de edificio sin climatizar en verano; editable en la pagina.</summary>
        public static readonly AmbienteCondensacion PorDefecto = new AmbienteCondensacion(25, 70);

        public double PuntoRocio => RiteAislamiento.PuntoRocio(Temperatura, HumedadRelativa);

        public static bool EsValido(double temperatura, double humedadRelativa)
            => temperatura > 0 && temperatura <= 50 && humedadRelativa > 0 && humedadRelativa < 100;
    }

    public static class RiteAislamiento
    {
        /// <summary>
        /// Criterio de E3G para el agua fria sanitaria: 9 mm con cualquier diametro,
        /// siempre que la comprobacion de condensacion lo valide (ver EspesorAguaFria).
        /// </summary>
        public const int EspesorAguaFriaMm = 9;

        /// <summary>Espesores de coquilla elastomerica de catalogo, en mm.</summary>
        public static readonly int[] EspesoresComerciales = { 9, 13, 19, 25, 32, 40, 50 };

        /// <summary>
        /// Gas caliente de una bomba de calor en modo calefaccion, para la condicion
        /// caliente de las lineas frigorificas reversibles (columna «> 60…100» del RITE).
        /// </summary>
        public const double TemperaturaGasCaliente = 70;

        public const double LambdaReferencia = 0.040;   // W/(m·K) a 10 °C, la del RITE
        public const double EmisividadAcabado = 0.9;    // acabado no metalico (elastomero, PVC)
        private const double Stefan = 5.67e-8;

        public static readonly double[] DiametrosAguaFria = { 16, 25, 40, 63, 110, 160 };
        public static readonly double[] HumedadesAguaFria = { 60, 65, 70, 75, 80 };

        public static readonly string[] FilasDiametro =
        {
            "D ≤ 35", "35 < D ≤ 60", "60 < D ≤ 90", "90 < D ≤ 140", "140 < D"
        };

        private const string EncCaliente = "Temperatura máxima del fluido (°C)";
        private const string EncFrio = "Temperatura mínima del fluido (°C)";
        private static readonly string[] ColCaliente = { "40…60", "> 60…100", "> 100…180" };
        private static readonly string[] ColFrio = { "> -10…0", "> 0…10", "> 10" };

        private static readonly TablaRite CalienteInterior = new TablaRite("1.2.4.2.1",
            "Fluidos calientes · interior de edificios", EncCaliente, ColCaliente,
            new[,] { { 25, 25, 30 }, { 30, 30, 40 }, { 30, 30, 40 }, { 30, 40, 50 }, { 35, 40, 50 } });

        private static readonly TablaRite CalienteExterior = new TablaRite("1.2.4.2.2",
            "Fluidos calientes · exterior de edificios", EncCaliente, ColCaliente,
            new[,] { { 35, 35, 40 }, { 40, 40, 50 }, { 40, 40, 50 }, { 40, 50, 60 }, { 45, 50, 60 } });

        private static readonly TablaRite FrioInterior = new TablaRite("1.2.4.2.3",
            "Fluidos fríos · interior de edificios", EncFrio, ColFrio,
            new[,] { { 30, 25, 20 }, { 40, 30, 20 }, { 40, 30, 30 }, { 50, 40, 30 }, { 50, 40, 30 } });

        private static readonly TablaRite FrioExterior = new TablaRite("1.2.4.2.4",
            "Fluidos fríos · exterior de edificios", EncFrio, ColFrio,
            new[,] { { 50, 45, 40 }, { 60, 50, 40 }, { 60, 50, 50 }, { 70, 60, 50 }, { 70, 60, 50 } });

        private static readonly TablaRite AcsInterior = new TablaRite("1.2.4.2",
            "ACS · interior de edificios", "Independiente de la temperatura", new[] { "ACS" },
            new[,] { { 30 }, { 35 }, { 35 }, { 45 }, { 45 } });

        private static readonly TablaRite AcsExterior = new TablaRite("1.2.4.2",
            "ACS · exterior de edificios", "Independiente de la temperatura", new[] { "ACS" },
            new[,] { { 40 }, { 45 }, { 45 }, { 55 }, { 55 } });

        public static TablaRite Tabla(FluidoRite fluido, UbicacionRite ubicacion)
        {
            bool interior = ubicacion == UbicacionRite.Interior;
            switch (fluido)
            {
                case FluidoRite.Frio: return interior ? FrioInterior : FrioExterior;
                case FluidoRite.Caliente: return interior ? CalienteInterior : CalienteExterior;
                case FluidoRite.Acs: return interior ? AcsInterior : AcsExterior;
                default: throw new ArgumentOutOfRangeException(nameof(fluido),
                    "El agua fría de fontanería y las líneas frigoríficas no tienen tabla propia.");
            }
        }

        /// <summary>
        /// Temperatura propuesta al detectar el sistema; editable en la pagina. En las
        /// lineas frigorificas es la de evaporacion (lado frio).
        /// </summary>
        public static double TemperaturaPorDefecto(FluidoRite fluido)
        {
            switch (fluido)
            {
                case FluidoRite.Frio:
                case FluidoRite.Afs:
                    return 10;
                case FluidoRite.Refrigerante:
                    return 5;
                default:
                    return 60;
            }
        }

        public static int IndiceDiametro(double diametroExteriorMm)
        {
            // El diametro llega de pies a mm: sin redondear, 35 podria caer en la fila siguiente.
            double d = Math.Round(diametroExteriorMm, 1);
            if (d <= 35) return 0;
            if (d <= 60) return 1;
            if (d <= 90) return 2;
            if (d <= 140) return 3;
            return 4;
        }

        /// <summary>Columna de la tabla, o -1 si la temperatura queda fuera de ella.</summary>
        public static int IndiceTemperatura(FluidoRite fluido, double temperatura)
        {
            switch (fluido)
            {
                case FluidoRite.Caliente:
                    if (temperatura < 40 || temperatura > 180) return -1;
                    if (temperatura <= 60) return 0;
                    return temperatura <= 100 ? 1 : 2;
                case FluidoRite.Frio:
                case FluidoRite.Refrigerante:   // columna del lado frio
                    if (temperatura <= -10) return -1;
                    if (temperatura <= 0) return 0;
                    return temperatura <= 10 ? 1 : 2;
                case FluidoRite.Afs:
                    return temperatura > 0 && temperatura < 40 ? 0 : -1;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Espesor minimo en mm, o null si la temperatura queda fuera de la tabla o si, en
        /// agua fria, ningun espesor comercial evita la condensacion con ese ambiente.
        /// </summary>
        public static int? Espesor(FluidoRite fluido, UbicacionRite ubicacion,
                                   double temperatura, double diametroExteriorMm,
                                   AmbienteCondensacion ambiente = null)
        {
            int columna = IndiceTemperatura(fluido, temperatura);
            if (columna < 0) return null;
            if (fluido == FluidoRite.Afs)
                return EspesorAguaFria(temperatura, diametroExteriorMm, ambiente ?? AmbienteCondensacion.PorDefecto);
            if (fluido == FluidoRite.Refrigerante)
                return EspesorRefrigerante(ubicacion, temperatura, diametroExteriorMm);
            return Tabla(fluido, ubicacion).Valores[IndiceDiametro(diametroExteriorMm), columna];
        }

        /// <summary>
        /// Linea frigorifica reversible (split, VRV): lleva frio o gas caliente segun el
        /// modo, asi que manda la condicion mas exigente de las dos tablas (RITE IT
        /// 1.2.4.2.1.2 ap. 4). Null si la evaporacion queda fuera de la tabla de frio.
        /// </summary>
        public static int? EspesorRefrigerante(UbicacionRite ubicacion, double temperaturaEvaporacion,
                                               double diametroExteriorMm)
        {
            int columnaFrio = IndiceTemperatura(FluidoRite.Frio, temperaturaEvaporacion);
            if (columnaFrio < 0) return null;
            int fila = IndiceDiametro(diametroExteriorMm);
            int frio = Tabla(FluidoRite.Frio, ubicacion).Valores[fila, columnaFrio];
            int caliente = Tabla(FluidoRite.Caliente, ubicacion)
                .Valores[fila, IndiceTemperatura(FluidoRite.Caliente, TemperaturaGasCaliente)];
            return Math.Max(frio, caliente);
        }

        // ── Agua fria sanitaria: condensacion superficial (UNE-EN ISO 12241) ──

        /// <summary>
        /// 9 mm si con ese espesor la superficie queda por encima del rocio; si no, el
        /// primer espesor comercial que lo consigue; null si no lo consigue ninguno.
        /// </summary>
        public static int? EspesorAguaFria(double temperaturaAgua, double diametroExteriorMm,
                                           AmbienteCondensacion ambiente)
        {
            foreach (int espesor in EspesoresComerciales)
                if (EvitaCondensacion(diametroExteriorMm, espesor, temperaturaAgua, ambiente))
                    return espesor;
            return null;
        }

        public static bool EvitaCondensacion(double diametroExteriorMm, double espesorMm,
                                             double temperaturaAgua, AmbienteCondensacion ambiente)
        {
            double rocio = ambiente.PuntoRocio;
            if (temperaturaAgua >= rocio) return true;
            return TemperaturaSuperficial(diametroExteriorMm, espesorMm, temperaturaAgua,
                                          ambiente.Temperatura) >= rocio;
        }

        /// <summary>Punto de rocio en °C (formula de Magnus sobre agua).</summary>
        public static double PuntoRocio(double temperatura, double humedadRelativa)
        {
            const double a = 17.62, b = 243.12;
            double g = Math.Log(humedadRelativa / 100.0) + a * temperatura / (b + temperatura);
            return b * g / (a - g);
        }

        /// <summary>
        /// Temperatura de la cara exterior del aislamiento en °C, aire en calma. Se toma la
        /// tuberia a la temperatura del agua (sin resistencia de pared ni de pelicula
        /// interior), lambda de referencia del RITE y el coeficiente superficial mas
        /// desfavorable entre tramo horizontal y vertical: todo ello del lado de la seguridad.
        /// </summary>
        public static double TemperaturaSuperficial(double diametroExteriorMm, double espesorMm,
                                                    double temperaturaAgua, double temperaturaAire,
                                                    double lambda = LambdaReferencia)
        {
            double di = diametroExteriorMm / 1000.0;
            double de = di + 2 * espesorMm / 1000.0;
            double resistenciaAislante = Math.Log(de / di) / (2 * Math.PI * lambda);   // m·K/W

            double superficie = temperaturaAire - 1;
            for (int i = 0; i < 100; i++)
            {
                double h = CoeficienteSuperficial(superficie, temperaturaAire, de);
                double resistenciaSuperficial = 1 / (h * Math.PI * de);
                double flujo = (temperaturaAire - temperaturaAgua) / (resistenciaAislante + resistenciaSuperficial);
                double nueva = temperaturaAire - flujo * resistenciaSuperficial;
                if (Math.Abs(nueva - superficie) < 1e-6) return nueva;
                superficie = nueva;
            }
            return superficie;
        }

        /// <summary>Radiacion + conveccion natural, formulas de la UNE-EN ISO 12241, en W/(m²·K).</summary>
        private static double CoeficienteSuperficial(double superficie, double aire, double diametroExteriorM)
        {
            double ts = superficie + 273.15, ta = aire + 273.15;
            double radiacion = Math.Abs(ts - ta) < 1e-9
                ? 4 * EmisividadAcabado * Stefan * ta * ta * ta
                : EmisividadAcabado * Stefan * (Math.Pow(ts, 4) - Math.Pow(ta, 4)) / (ts - ta);

            double dt = Math.Abs(aire - superficie);
            if (dt <= 0) return radiacion;
            double horizontal = Math.Pow(diametroExteriorM, 3) * dt <= 10
                ? 1.25 * Math.Pow(dt / diametroExteriorM, 0.25)   // laminar
                : 1.21 * Math.Pow(dt, 1.0 / 3);                    // turbulento
            double vertical = 1.74 * Math.Pow(dt, 1.0 / 3);        // montante alto, turbulento
            return radiacion + Math.Min(horizontal, vertical);
        }

        /// <summary>Espesor que coloca la app en agua fria, por diametro y humedad, para la ventana de tablas.</summary>
        public static TablaRite TablaAguaFria(double temperaturaAgua, double temperaturaAire)
        {
            var valores = new int[DiametrosAguaFria.Length, HumedadesAguaFria.Length];
            for (int f = 0; f < DiametrosAguaFria.Length; f++)
                for (int c = 0; c < HumedadesAguaFria.Length; c++)
                    valores[f, c] = EspesorAguaFria(temperaturaAgua, DiametrosAguaFria[f],
                                        new AmbienteCondensacion(temperaturaAire, HumedadesAguaFria[c])) ?? 0;

            string Num(double v) => v.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
            return new TablaRite("AFS",
                $"Agua fría sanitaria · agua a {Num(temperaturaAgua)} °C, aire a {Num(temperaturaAire)} °C",
                "Humedad relativa del aire",
                Array.ConvertAll(HumedadesAguaFria, h => Num(h) + " %"),
                valores,
                Array.ConvertAll(DiametrosAguaFria, Num),
                "Anticondensación · UNE-EN ISO 12241");
        }
    }
}
