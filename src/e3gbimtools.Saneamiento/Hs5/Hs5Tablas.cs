using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// =============================================================================
// CTE DB-HS5 — TABLAS DE DIMENSIONADO (sección 4)
// -----------------------------------------------------------------------------
// Datos y consultas puros, sin dependencia de la API de Revit, para poder
// testearlos fuera de Revit (Tests\e3gbimtools.Hs5.Tests enlaza este fichero).
// Fuente: Documento Básico HS Salubridad con comentarios, HS 5, págs. 125-127.
// =============================================================================
namespace E3G.Saneamiento.Hs5
{
    /// <summary>
    /// Tabla de dimensionado "valor máximo admisible por DN": para cada DN
    /// (fila, ascendente) y cada columna (pendiente, nº de plantas…) indica el
    /// valor máximo de la magnitud de entrada (UD, m²…). NaN = no admitido ("–").
    /// </summary>
    public sealed class TablaHs5
    {
        public string Id { get; }
        public string Titulo { get; }
        public string Descripcion { get; }
        public string Magnitud { get; }
        public string[] Columnas { get; }
        /// <summary>Pendiente (%) de cada columna, o null si la tabla no va por pendiente.</summary>
        public double[] PendientesColumna { get; }
        public int[] DN { get; }
        public double[,] Max { get; }

        public TablaHs5(string id, string titulo, string descripcion, string magnitud,
                        string[] columnas, double[] pendientesColumna, int[] dn, double[,] max)
        {
            if (max.GetLength(0) != dn.Length || max.GetLength(1) != columnas.Length)
                throw new ArgumentException($"Tabla {id}: dimensiones de datos incoherentes.");
            Id = id; Titulo = titulo; Descripcion = descripcion; Magnitud = magnitud;
            Columnas = columnas; PendientesColumna = pendientesColumna; DN = dn; Max = max;
        }

        public string Nombre => $"Tabla {Id} CTE DB-HS5";
        public bool VaPorPendiente => PendientesColumna != null;

        /// <summary>DN mínimo que admite <paramref name="valor"/> en la columna dada.</summary>
        public ConsultaHs5 Consultar(double valor, int columna)
        {
            if (columna < 0 || columna >= Columnas.Length)
                throw new ArgumentOutOfRangeException(nameof(columna));

            int ultimoAdmitido = 0;
            for (int i = 0; i < DN.Length; i++)
            {
                double max = Max[i, columna];
                if (double.IsNaN(max)) continue;
                ultimoAdmitido = DN[i];
                if (valor <= max)
                    return new ConsultaHs5(DN[i], max, false);
            }
            // Supera el máximo de la tabla: se devuelve el mayor DN admitido como referencia.
            return new ConsultaHs5(ultimoAdmitido, double.NaN, true);
        }

        /// <summary>
        /// Columna a usar para una pendiente real: la de mayor pendiente que no la
        /// supere (criterio conservador). Si la pendiente es menor que la mínima de
        /// la tabla se devuelve la primera columna e <paramref name="insuficiente"/> = true.
        /// </summary>
        public int ColumnaPorPendiente(double pendientePct, out bool insuficiente)
        {
            if (!VaPorPendiente)
                throw new InvalidOperationException($"La tabla {Id} no va por pendiente.");

            const double tolerancia = 0.05; // puntos %, absorbe redondeos del modelado
            int col = -1;
            for (int i = 0; i < PendientesColumna.Length; i++)
                if (PendientesColumna[i] <= pendientePct + tolerancia) col = i;

            insuficiente = col < 0;
            return insuficiente ? 0 : col;
        }

        /// <summary>Texto de la celda para mostrar la tabla ("–" si no admitido).</summary>
        public string Celda(int fila, int columna)
        {
            double v = Max[fila, columna];
            return double.IsNaN(v) ? "–" : v.ToString("#,##0.##", Hs5Tablas.Es);
        }
    }

    public sealed class ConsultaHs5
    {
        public int DNMinimo { get; }
        /// <summary>Límite de la fila elegida (NaN si fuera de tabla).</summary>
        public double Limite { get; }
        public bool FueraDeTabla { get; }

        public ConsultaHs5(int dnMinimo, double limite, bool fueraDeTabla)
        {
            DNMinimo = dnMinimo; Limite = limite; FueraDeTabla = fueraDeTabla;
        }
    }

    /// <summary>Fila de la tabla 4.1 (UD y diámetro mínimo de sifón por aparato).</summary>
    public sealed class AparatoHs5
    {
        public string Aparato { get; }
        public string Variante { get; }
        /// <summary>NaN = no aplica en ese uso.</summary>
        public double UDPrivado { get; }
        public double UDPublico { get; }
        /// <summary>0 = no aplica.</summary>
        public int DNPrivado { get; }
        public int DNPublico { get; }

        public AparatoHs5(string aparato, string variante, double udPriv, double udPub, int dnPriv, int dnPub)
        {
            Aparato = aparato; Variante = variante;
            UDPrivado = udPriv; UDPublico = udPub; DNPrivado = dnPriv; DNPublico = dnPub;
        }

        public string Nombre => string.IsNullOrEmpty(Variante) ? Aparato : $"{Aparato} · {Variante}";
    }

    public enum UsoHs5 { Privado, Publico }

    public static class Hs5Tablas
    {
        internal static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
        private const double X = double.NaN; // "–" en la norma

        /// <summary>
        /// Diámetro comercial que sustituye al ø100 de las tablas (no existe en PVC de evacuación).
        /// Se exige ø110 donde la norma pide ø100 y un tubo ø110 se busca en las filas de ø100.
        /// </summary>
        public const int DN100 = 110;

        /// <summary>DN con el que se entra en una tabla que tiene fila de ø100: ø110 comercial → 100.</summary>
        public static int DNNorma(int dn) => dn == DN100 ? 100 : dn;

        private static readonly double[] Pend124 = { 1, 2, 4 };
        private static readonly string[] ColPend124 = { "1 %", "2 %", "4 %" };

        // ── Tabla 4.3 · Ramales colectores entre aparatos sanitarios y bajante ──
        public static readonly TablaHs5 T4_3 = new TablaHs5(
            "4.3", "Ramales colectores",
            "Ramales entre aparatos sanitarios y bajante · máx. UD según pendiente",
            "UD", ColPend124, Pend124,
            new[] { 32, 40, 50, 63, 75, 90, 110, 125, 160, 200 },
            new double[,]
            {
                {   X,    1,    1 },
                {   X,    2,    3 },
                {   X,    6,    8 },
                {   X,   11,   14 },
                {   X,   21,   28 },
                {  47,   60,   75 },
                { 123,  151,  181 },
                { 180,  234,  280 },
                { 438,  582,  800 },
                { 870, 1150, 1680 },
            });

        // ── Tabla 4.4 · Bajantes de aguas residuales ──
        // Columnas 0-1: máx. UD en la bajante; 2-3: máx. UD en cada ramal.
        // El DN es el mayor de los obtenidos con ambos criterios (apdo. 4.1.2.2).
        public const int ColBajanteHasta3 = 0, ColBajanteMas3 = 1, ColRamalHasta3 = 2, ColRamalMas3 = 3;

        public static readonly TablaHs5 T4_4 = new TablaHs5(
            "4.4", "Bajantes",
            "Bajantes de aguas residuales · máx. UD según nº de plantas",
            "UD",
            new[] { "Bajante ≤ 3 pl.", "Bajante > 3 pl.", "Ramal ≤ 3 pl.", "Ramal > 3 pl." },
            null,
            new[] { 50, 63, 75, 90, 110, 125, 160, 200, 250, 315 },
            new double[,]
            {
                {   10,   25,    6,    6 },
                {   19,   38,   11,    9 },
                {   27,   53,   21,   13 },
                {  135,  280,   70,   53 },
                {  360,  740,  181,  134 },
                {  540, 1100,  280,  200 },
                { 1208, 2240, 1120,  400 },
                { 2200, 3600, 1680,  600 },
                { 3800, 5600, 2500, 1000 },
                { 6000, 9240, 4320, 1650 },
            });

        // ── Tabla 4.5 · Colectores horizontales de aguas residuales ──
        public static readonly TablaHs5 T4_5 = new TablaHs5(
            "4.5", "Colectores horizontales",
            "Colectores horizontales de aguas residuales · máx. UD según pendiente",
            "UD", ColPend124, Pend124,
            new[] { 50, 63, 75, 90, 110, 125, 160, 200, 250, 315, 350 },
            new double[,]
            {
                {    X,    20,    25 },
                {    X,    24,    29 },
                {    X,    38,    57 },
                {   96,   130,   160 },
                {  264,   321,   382 },
                {  390,   480,   580 },
                {  880,  1056,  1300 },
                { 1600,  1920,  2300 },
                { 2900,  3500,  4200 },
                { 5710,  6920,  8290 },
                { 8300, 10000, 12000 },
            });

        // ── Tabla 4.7 · Canalones (100 mm/h) ──
        public static readonly TablaHs5 T4_7 = new TablaHs5(
            "4.7", "Canalones",
            "Canalón semicircular · máx. superficie de cubierta (m²) según pendiente, 100 mm/h",
            "m²", new[] { "0,5 %", "1 %", "2 %", "4 %" }, new double[] { 0.5, 1, 2, 4 },
            new[] { 100, 125, 150, 200, 250 },
            new double[,]
            {
                {  35,  45,  65,  95 },
                {  60,  80, 115, 165 },
                {  90, 125, 175, 255 },
                { 185, 260, 370, 520 },
                { 335, 475, 670, 930 },
            });

        // ── Tabla 4.8 · Bajantes de aguas pluviales (100 mm/h) ──
        public static readonly TablaHs5 T4_8 = new TablaHs5(
            "4.8", "Bajantes pluviales",
            "Bajantes de aguas pluviales · máx. superficie servida (m²), 100 mm/h",
            "m²", new[] { "100 mm/h" }, null,
            new[] { 50, 63, 75, 90, 110, 125, 160, 200 },
            new double[,] { { 65 }, { 113 }, { 177 }, { 318 }, { 580 }, { 805 }, { 1544 }, { 2700 } });

        // ── Tabla 4.9 · Colectores de aguas pluviales y mixtos (100 mm/h) ──
        public static readonly TablaHs5 T4_9 = new TablaHs5(
            "4.9", "Colectores pluviales",
            "Colectores pluviales y mixtos · máx. superficie (m²) según pendiente, 100 mm/h",
            "m²", ColPend124, Pend124,
            new[] { 90, 110, 125, 160, 200, 250, 315 },
            new double[,]
            {
                {  125,  178,  253 },
                {  229,  323,  458 },
                {  310,  440,  620 },
                {  614,  862, 1228 },
                { 1070, 1510, 2140 },
                { 1920, 2710, 3850 },
                { 2016, 4589, 6500 },
            });

        /// <summary>Tablas de verificación de tuberías, en el orden del selector.</summary>
        public static readonly IReadOnlyList<TablaHs5> Verificacion = new[] { T4_3, T4_4, T4_5, T4_8, T4_9 };

        /// <summary>Todas las tablas "máximo por DN" (incluye 4.7, que no se aplica a tuberías).</summary>
        public static readonly IReadOnlyList<TablaHs5> PorDN = new[] { T4_3, T4_4, T4_5, T4_7, T4_8, T4_9 };

        // ── Tabla 4.6 · Número de sumideros ──
        public static int NumeroSumideros(double superficieM2)
        {
            if (superficieM2 < 100) return 2;
            if (superficieM2 < 200) return 3;
            if (superficieM2 < 500) return 4;
            return (int)Math.Ceiling(superficieM2 / 150.0);
        }

        // ── 4.3 · Colectores mixtos: UD → superficie equivalente (100 mm/h) ──
        public static double SuperficieEquivalente(double ud) =>
            ud <= 0 ? 0 : ud <= 250 ? 90 : 0.36 * ud;

        // ── Anejo B · Tabla B.1 · Intensidad pluviométrica (mm/h) ──
        public static readonly int[] Isoyetas = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
        private static readonly int[] IntensidadA = { 30, 65, 90, 125, 155, 180, 210, 240, 275, 300, 330, 365 };
        private static readonly int[] IntensidadB = { 30, 50, 70, 90, 110, 135, 150, 170, 195, 220, 240, 265 };

        /// <summary>Intensidad i (mm/h) para la zona (A/B) e isoyeta; isoyetas intermedias toman la superior.</summary>
        public static double Intensidad(char zona, int isoyeta)
        {
            var tabla = char.ToUpperInvariant(zona) == 'B' ? IntensidadB : IntensidadA;
            for (int k = 0; k < Isoyetas.Length; k++)
                if (isoyeta <= Isoyetas[k]) return tabla[k];
            return tabla[tabla.Length - 1];
        }

        /// <summary>Factor de corrección f = i / 100 (4.2.2.2).</summary>
        public static double FactorPluviometrico(char zona, int isoyeta) => Intensidad(zona, isoyeta) / 100.0;

        // ── Tabla 4.10 · Ventilación secundaria ──
        public static readonly int[] T4_10_DNVentilacion = { 32, 40, 50, 63, 65, 80, DN100, 125, 150, 200 };

        // (DN bajante, UD máx., máx. longitud efectiva (m) por DN de ventilación; NaN = no admitido)
        public static readonly IReadOnlyList<(int Bajante, double UD, double[] Long)> T4_10 = new[]
        {
            (32,    2.0, F(9)),
            (40,    8.0, F(15, 45)),
            (50,   10.0, F(9, 30)),
            (50,   24.0, F(7, 14, 40)),
            (63,   19.0, F(X, 13, 38, 100)),
            (63,   40.0, F(X, 10, 32, 90)),
            (75,   27.0, F(X, 10, 25, 68, 130)),
            (75,   54.0, F(X, 8, 20, 63, 120)),
            (90,   65.0, F(X, X, 14, 30, 93, 175)),
            (90,  153.0, F(X, X, 12, 26, 58, 145)),
            (110,  180.0, F(X, X, X, 15, 56, 97, 290)),
            (110,  360.0, F(X, X, X, 10, 51, 79, 270)),
            (110,  740.0, F(X, X, X, 8, 48, 73, 220)),
            (125,  300.0, F(X, X, X, 6, 45, 65, 100, 300)),
            (125,  540.0, F(X, X, X, X, 42, 57, 85, 250)),
            (125, 1100.0, F(X, X, X, X, 40, 47, 70, 210)),
            (160,  696.0, F(X, X, X, X, X, 32, 47, 100, 340)),
            (160, 1048.0, F(X, X, X, X, X, 31, 40, 90, 310)),
            (160, 1960.0, F(X, X, X, X, X, 25, 34, 60, 220)),
            (200, 1000.0, F(X, X, X, X, X, X, 28, 37, 202, 380)),
            (200, 1400.0, F(X, X, X, X, X, X, 25, 30, 185, 360)),
            (200, 2200.0, F(X, X, X, X, X, X, 19, 22, 157, 330)),
            (200, 3600.0, F(X, X, X, X, X, X, 18, 20, 150, 250)),
            (250, 2500.0, F(X, X, X, X, X, X, 10, 18, 75, 150)),
            (250, 3800.0, F(X, X, X, X, X, X, X, 16, 40, 105)),
            (250, 5600.0, F(X, X, X, X, X, X, X, 14, 25, 75)),
            (315, 4450.0, F(X, X, X, X, X, X, X, 7, 8, 15)),
            (315, 6508.0, F(X, X, X, X, X, X, X, 6, 7, 12)),
            (315, 9046.0, F(X, X, X, X, X, X, X, 5, 6, 10)),
        };

        /// <summary>
        /// DN mínimo de la columna de ventilación secundaria (tabla 4.10). Bajantes de DN
        /// intermedio toman el grupo superior; la UD, la primera fila que la cubre.
        /// </summary>
        public static ConsultaHs5 VentilacionSecundaria(int dnBajante, double ud, double longitudEfectivaM)
        {
            int grupo = T4_10.Select(r => r.Bajante).Where(d => d >= dnBajante).DefaultIfEmpty(0).Min();
            if (grupo == 0) return new ConsultaHs5(T4_10_DNVentilacion.Last(), double.NaN, true);
            var filas = T4_10.Where(r => r.Bajante == grupo).ToList();
            var fila = filas.FirstOrDefault(r => ud <= r.UD);
            bool fuera = fila.Long == null;
            if (fuera) fila = filas.Last();
            for (int c = 0; c < fila.Long.Length; c++)
                if (!double.IsNaN(fila.Long[c]) && longitudEfectivaM <= fila.Long[c])
                    return new ConsultaHs5(T4_10_DNVentilacion[c], fila.Long[c], fuera);
            return new ConsultaHs5(T4_10_DNVentilacion[fila.Long.Length - 1], double.NaN, true);
        }

        // ── Tabla 4.11 · Ventilación secundaria con uniones en cada planta ──
        public static readonly IReadOnlyList<(int Bajante, int Ventilacion)> T4_11 = new[]
        {
            (40, 32), (50, 32), (63, 40), (75, 40), (90, 50), (110, 63),
            (125, 75), (160, 90), (200, 110), (250, 125), (315, 160),
        };

        public static int VentilacionCadaPlanta(int dnBajante)
        {
            foreach (var (b, v) in T4_11) if (dnBajante <= b) return v;
            return T4_11[T4_11.Count - 1].Ventilacion;
        }

        // ── Tabla 4.12 · Ventilación terciaria ──
        public static readonly int[] T4_12_DNVentilacion = { 32, 40, 50, 65, 80 };
        private const double Mas300 = double.PositiveInfinity; // ">300" en la norma

        // (DN ramal de desagüe, pendiente %, máx. longitud (m) por DN de ventilación)
        public static readonly IReadOnlyList<(int Ramal, double Pendiente, double[] Long)> T4_12 = new[]
        {
            (32, 2.0, F(Mas300)),
            (40, 2.0, F(Mas300, Mas300)),
            (50, 1.0, F(Mas300, Mas300, Mas300)),
            (50, 2.0, F(Mas300, Mas300, Mas300)),
            (65, 1.0, F(300, Mas300, Mas300, Mas300)),
            (65, 2.0, F(250, Mas300, Mas300, Mas300)),
            (80, 1.0, F(200, 300, Mas300, Mas300, Mas300)),
            (80, 2.0, F(100, 215, Mas300, Mas300, Mas300)),
            (100, 1.0, F(40, 110, 300, Mas300, Mas300)),
            (100, 2.0, F(20, 44, 180, Mas300, Mas300)),
            (125, 1.0, F(X, 28, 107, 255, Mas300)),
            (125, 2.0, F(X, 15, 48, 125, Mas300)),
            (150, 1.0, F(X, X, 37, 96, Mas300)),
            (150, 2.0, F(X, X, 18, 47, Mas300)),
        };

        /// <summary>
        /// DN mínimo de la ventilación terciaria (tabla 4.12). Ramal de DN intermedio →
        /// grupo superior; pendiente &gt; 1,5 % (o grupo sin fila al 1 %) → fila del 2 %,
        /// que admite longitudes menores (conservador).
        /// </summary>
        public static ConsultaHs5 VentilacionTerciaria(int dnRamal, double pendientePct, double longitudM)
        {
            dnRamal = DNNorma(dnRamal);
            int grupo = T4_12.Select(r => r.Ramal).Where(d => d >= dnRamal).DefaultIfEmpty(0).Min();
            if (grupo == 0) return new ConsultaHs5(T4_12_DNVentilacion.Last(), double.NaN, true);
            var filas = T4_12.Where(r => r.Ramal == grupo).ToList();
            var fila = pendientePct > 1.5 || filas.All(r => r.Pendiente != 1.0)
                ? filas.First(r => r.Pendiente == 2.0)
                : filas.First(r => r.Pendiente == 1.0);
            for (int c = 0; c < fila.Long.Length; c++)
                if (!double.IsNaN(fila.Long[c]) && longitudM <= fila.Long[c])
                    return new ConsultaHs5(T4_12_DNVentilacion[c], fila.Long[c], false);
            return new ConsultaHs5(T4_12_DNVentilacion[fila.Long.Length - 1], double.NaN, true);
        }

        // ── Tabla 4.13 · Dimensiones mínimas de arquetas (L × A, cm) ──
        public static readonly IReadOnlyList<(int DNSalida, int L, int A)> T4_13 = new[]
        {
            (100, 40, 40), (150, 50, 50), (200, 60, 60), (250, 60, 70), (300, 70, 70),
            (350, 70, 80), (400, 80, 80), (450, 80, 90), (500, 90, 90),
        };

        /// <summary>L × A mínimos para el DN del colector de salida (DN intermedio → fila superior).</summary>
        public static (int L, int A, bool Fuera) Arqueta(int dnSalida)
        {
            dnSalida = DNNorma(dnSalida);
            foreach (var r in T4_13) if (dnSalida <= r.DNSalida) return (r.L, r.A, false);
            var u = T4_13[T4_13.Count - 1];
            return (u.L, u.A, true);
        }

        private static double[] F(params double[] v) => v;

        public static TablaHs5 PorId(string id) => Verificacion.FirstOrDefault(t => t.Id == id);

        /// <summary>Columna de la tabla 4.4 según nº de plantas.</summary>
        public static int ColumnaBajante(int plantas, bool criterioRamal)
        {
            bool mas3 = plantas > 3;
            return criterioRamal ? (mas3 ? ColRamalMas3 : ColRamalHasta3)
                                 : (mas3 ? ColBajanteMas3 : ColBajanteHasta3);
        }

        // ── Tabla 4.1 · UD por aparato sanitario ──
        // El ø100 de la norma no es un diámetro comercial en PVC: se exige ø110 (DN100).
        public static readonly IReadOnlyList<AparatoHs5> T4_1 = new[]
        {
            new AparatoHs5("Lavabo", "", 1, 2, 32, 40),
            new AparatoHs5("Bidé", "", 2, 3, 32, 40),
            new AparatoHs5("Ducha", "", 2, 3, 40, 50),
            new AparatoHs5("Bañera (con o sin ducha)", "", 3, 4, 40, 50),
            new AparatoHs5("Inodoro", "Con cisterna", 4, 5, DN100, DN100),
            new AparatoHs5("Inodoro", "Con fluxómetro", 8, 10, DN100, DN100),
            new AparatoHs5("Urinario", "Pedestal", X, 4, 0, 50),
            new AparatoHs5("Urinario", "Suspendido", X, 2, 0, 40),
            new AparatoHs5("Urinario", "En batería", X, 3.5, 0, 0),
            new AparatoHs5("Fregadero", "De cocina", 3, 6, 40, 50),
            new AparatoHs5("Fregadero", "De laboratorio, restaurante, etc.", X, 2, 0, 40),
            new AparatoHs5("Lavadero", "", 3, X, 40, 0),
            new AparatoHs5("Vertedero", "", X, 8, 0, DN100),
            new AparatoHs5("Fuente para beber", "", X, 0.5, 0, 25),
            new AparatoHs5("Sumidero sifónico", "", 1, 3, 40, 50),
            new AparatoHs5("Lavavajillas", "", 3, 6, 40, 50),
            new AparatoHs5("Lavadora", "", 3, 6, 40, 50),
            new AparatoHs5("Cuarto de baño (lavabo, inodoro, bañera y bidé)", "Inodoro con cisterna", 7, X, DN100, 0),
            new AparatoHs5("Cuarto de baño (lavabo, inodoro, bañera y bidé)", "Inodoro con fluxómetro", 8, X, DN100, 0),
            new AparatoHs5("Cuarto de aseo (lavabo, inodoro y ducha)", "Inodoro con cisterna", 6, X, DN100, 0),
            new AparatoHs5("Cuarto de aseo (lavabo, inodoro y ducha)", "Inodoro con fluxómetro", 8, X, DN100, 0),
        };

        // ── Tabla 4.2 · UD de otros aparatos según diámetro del desagüe ──
        public static readonly IReadOnlyList<(int DN, double UD)> T4_2 = new[]
        {
            (32, 1.0), (40, 2.0), (50, 3.0), (60, 4.0), (80, 5.0), (100, 6.0),
        };

        /// <summary>
        /// UD de un aparato no incluido en la tabla 4.1 según el diámetro de su
        /// desagüe (tabla 4.2), tomando la fila inmediatamente superior.
        /// </summary>
        public static double UDPorDiametroDesague(int dnDesague)
        {
            dnDesague = DNNorma(dnDesague);
            foreach (var (dn, ud) in T4_2)
                if (dnDesague <= dn) return ud;
            return T4_2[T4_2.Count - 1].UD;
        }

        /// <summary>Desagües continuos o semicontinuos: 1 UD por cada 0,03 dm³/s (apdo. 4.1.1.1.2).</summary>
        public static double UDPorCaudalContinuo(double caudalDm3s) => caudalDm3s / 0.03;
    }

    /// <summary>Colores de coloreado por DN exigido (leyenda y overrides de vista).</summary>
    public static class Hs5Colores
    {
        private static readonly Dictionary<int, (string Hex, string Nombre)> PorDN =
            new Dictionary<int, (string, string)>
            {
                {  25, ("#FBCFE8", "Rosa claro") },
                {  32, ("#F2A7C8", "Rosa") },
                {  40, ("#94A3B8", "Gris claro") },
                {  50, ("#64748B", "Gris") },
                {  63, ("#2E9E5B", "Verde") },
                {  65, ("#3FB27F", "Verde medio") },
                {  75, ("#9ACD32", "Lima") },
                {  80, ("#C2D94C", "Oliva") },
                {  90, ("#E24B4A", "Rojo") },
                { 100, ("#F26B3A", "Coral") },
                { 110, ("#EF9F27", "Naranja") },
                { 125, ("#E8D800", "Amarillo") },
                { 150, ("#C77D3A", "Ocre") },
                { 160, ("#A0522D", "Marrón") },
                { 200, ("#378ADD", "Azul") },
                { 250, ("#7F77DD", "Violeta") },
                { 315, ("#1AB3C4", "Cian") },
                { 350, ("#E0E0E0", "Blanco") },
            };

        public static string Hex(int dn) => PorDN.TryGetValue(dn, out var c) ? c.Hex : "#E2E8F0";
        public static string Nombre(int dn) => PorDN.TryGetValue(dn, out var c) ? c.Nombre : "—";

        public static (int R, int G, int B) Rgb(int dn)
        {
            string h = Hex(dn).TrimStart('#');
            return (Convert.ToInt32(h.Substring(0, 2), 16),
                    Convert.ToInt32(h.Substring(2, 2), 16),
                    Convert.ToInt32(h.Substring(4, 2), 16));
        }
    }
}
