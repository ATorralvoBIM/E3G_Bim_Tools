// ============================================================================
//  Bc3Writer.cs
//  Escritor BC3 (FIEBDC-3/2024) — genera lo mínimo y necesario para que el
//  archivo abra limpio en Presto/Arquímedes/Menfis.
//
//  Convenciones críticas:
//    - Codificación CP1252.
//    - CRLF.
//    - Decimales con punto, no coma.
//    - Cada registro acaba con | tras el último campo.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using E3G.Saneamiento.Presupuesto.Modelo;

namespace E3G.Saneamiento.Presupuesto.Bc3
{
    public sealed class DatosCabecera
    {
        public string NombrePrograma   { get; init; } = "E3G_BIM_TOOLS";
        public string Empresa          { get; init; } = "E3G";
        public string Version          { get; init; } = "1.0";
        public DateTime Fecha          { get; init; } = DateTime.Now;
        public string FormatoDeclarado { get; init; } = "FIEBDC-3/2024";
        public string ProyectoNombre   { get; init; } = "PRESUPUESTO PROYECTO";
        public string CodigoRaiz       { get; init; } = "##";
    }

    public sealed class Bc3Writer
    {
        public void Escribir(
            string rutaSalida,
            Plantilla plantilla,
            IReadOnlyList<MedicionPorPartida> mediciones,
            DatosCabecera cabecera = null)
        {
            cabecera ??= new DatosCabecera();
            Bc3Encoding.RegistrarCodepageProvider();

            using var sw = new StreamWriter(rutaSalida, false, Bc3Encoding.ParaEscritura());
            sw.NewLine = "\r\n";

            // Filtrem mediciones efectives
            var medicionesEfectivas = mediciones
                .Where(m => m.LineasParaExportar.Any())
                .ToList();

            // Pre-calculem les cantitats totals per partida (per usar al ~D)
            var totalsPerPartida = medicionesEfectivas.ToDictionary(
                m => m,
                m => m.LineasParaExportar.Sum(l => l.Cantidad));

            // ─── 1. Cabecera (~V, ~K) ─────────────────────────────────────
            EscribirRegistroV(sw, cabecera);
            EscribirRegistroK(sw);

            // ─── 2. Definicions ~C (capítols i partides) ──────────────────
            EscribirCapituloRaiz(sw, cabecera);
            EscribirCsDeCapitols(sw, medicionesEfectivas, cabecera);
            foreach (var m in medicionesEfectivas)
            {
                EscribirRegistroC_Partida(sw, m.Partida, cabecera);
                EscribirRegistroT(sw, m.Partida);
            }

            // ─── 3. Descomposicions ~D (UN ~D per pare amb tots els fills) ─
            // El RENDIMIENTO de cada fill ÉS la cantitat total mesurada,
            // perquè TCQ/Presto el fan servir com a "amidament" de la partida.
            EscribirDArrelACapitols(sw, medicionesEfectivas, cabecera);
            EscribirDsDeCapitolsAPartides(sw, medicionesEfectivas, totalsPerPartida);

            // ─── 4. Mediciones ~M (línies detallades per partida) ─────────
            foreach (var m in medicionesEfectivas)
            {
                var lineasEfectivas = m.LineasParaExportar.ToList();
                EscribirRegistroM(sw, m, lineasEfectivas);
            }

            sw.Flush();
        }

        // ─── ~V — Propiedad y versión ──────────────────────────────────────
        // Format FIEBDC-3 estàndard:
        //   ~V|<FITXER>|<VERSIO_FORMAT>|<PROGRAMA>|<CABECERA>|<JUEGO>|<NUM_DEC>|<TIPO>|<AGENT>|
        // Camps separats per | (no \). Camps opcionals poden anar buits.
        private static void EscribirRegistroV(StreamWriter sw, DatosCabecera c)
        {
            string fecha = c.Fecha.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
            string cabecera = $"{c.NombrePrograma} {c.Empresa} {c.Version} {fecha}";
            // 8 camps: fitxer, versio_format, programa, cabecera, joc, num_dec, tipo, agent
            sw.WriteLine($"~V|{c.NombrePrograma}.bc3|{c.FormatoDeclarado}|{c.NombrePrograma}|{cabecera}|ANSI|||");
        }

        // ─── ~K — Coeficientes (decimales) ─────────────────────────────────
        // Format: ~K|<DN>\<DD>\<DS>\<DR>\<DI>\<DP>\<DC>\<DM>\<DCH>|<MONEDA>|
        // Cal el camp MONEDA al final per a TCQ/Presto.
        private static void EscribirRegistroK(StreamWriter sw)
        {
            sw.WriteLine("~K|2\\2\\2\\2\\2\\2\\2\\2\\2|EUR|");
        }

        // Format ~C correcte (FIEBDC-3, sense camp MONEDA):
        //   ~C|<CODIGO>|<UNIDAD>|<RESUMEN>|<PRECIO>|<FECHA>|<TIPO>|
        // Tipus: 0 = capítol, 1 = partida (per defecte si camp buit).
        private static void EscribirCapituloRaiz(StreamWriter sw, DatosCabecera c)
        {
            string fecha = c.Fecha.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
            // CODIGO=##, UNIDAD=buit, RESUMEN=nom projecte, PRECIO=0, FECHA, TIPO=0 (capítol)
            sw.WriteLine($"~C|{c.CodigoRaiz}||{EscapeBc3(c.ProyectoNombre)}|0|{fecha}|0|");
        }

        // Escriu només els ~C dels capítols utilitzats (sense la descomposició,
        // que va separada per evitar múltiples ~D amb el mateix pare).
        private static void EscribirCsDeCapitols(
            StreamWriter sw,
            IReadOnlyList<MedicionPorPartida> mediciones,
            DatosCabecera c)
        {
            string fecha = c.Fecha.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
            var capitulos = ExtreureCapitols(mediciones);

            foreach (var cap in capitulos)
            {
                var codCap = cap.EndsWith("#") ? cap : cap + "##";
                sw.WriteLine($"~C|{codCap}||{EscapeBc3(NombreCapitulo(cap))}|0|{fecha}|0|");
            }
        }

        // ~D arrel: ## → totes les capítols utilitzats (UN sol ~D)
        private static void EscribirDArrelACapitols(
            StreamWriter sw,
            IReadOnlyList<MedicionPorPartida> mediciones,
            DatosCabecera c)
        {
            var capitulos = ExtreureCapitols(mediciones);
            if (capitulos.Count == 0) return;

            var sb = new StringBuilder();
            sb.Append($"~D|{c.CodigoRaiz}|");
            foreach (var cap in capitulos)
            {
                var codCap = cap.EndsWith("#") ? cap : cap + "##";
                sb.Append($"{codCap}\\1\\1\\");
            }
            sb.Append('|');
            sw.WriteLine(sb.ToString());
        }

        // ~D per cada capítol → totes les seves partides amb rendiment = cantitat total
        private static void EscribirDsDeCapitolsAPartides(
            StreamWriter sw,
            IReadOnlyList<MedicionPorPartida> mediciones,
            System.Collections.Generic.Dictionary<MedicionPorPartida, decimal> totals)
        {
            var perCapitol = mediciones
                .GroupBy(m => m.CapituloDestino.EndsWith("#")
                              ? m.CapituloDestino
                              : m.CapituloDestino + "##");

            foreach (var grup in perCapitol)
            {
                var sb = new StringBuilder();
                sb.Append($"~D|{grup.Key}|");
                foreach (var m in grup)
                {
                    var cantitat = totals.TryGetValue(m, out var c) ? c : 0m;
                    var cantitatStr = cantitat.ToString("0.###", CultureInfo.InvariantCulture);
                    sb.Append($"{m.Partida.Codigo}\\1\\{cantitatStr}\\");
                }
                sb.Append('|');
                sw.WriteLine(sb.ToString());
            }
        }

        private static System.Collections.Generic.List<string> ExtreureCapitols(
            IReadOnlyList<MedicionPorPartida> mediciones)
        {
            return mediciones
                .Where(m => !string.IsNullOrEmpty(m.CapituloDestino))
                .Select(m => m.CapituloDestino)
                .Distinct()
                .OrderBy(s => s)
                .ToList();
        }

        private static string NombreCapitulo(string cod) => cod.ToUpperInvariant() switch
        {
            "02SAN" => "SANEJAMENT",
            "02FON" => "FONTANERIA",
            "02PCI" => "PROTECCIÓ CONTRA INCENDIS",
            _       => cod
        };

        private static void EscribirRegistroC_Partida(StreamWriter sw, Partida p, DatosCabecera c)
        {
            string fecha = c.Fecha.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
            string precio = p.Precio.ToString("0.##", CultureInfo.InvariantCulture);
            // Partida: tipus buit (= 1 = partida per defecte). SENSE camp MONEDA.
            // ~C|CODIGO|UNIDAD|RESUMEN|PRECIO|FECHA|TIPO|
            sw.WriteLine(
                $"~C|{p.Codigo}|{p.Unidad}|{EscapeBc3(p.Descripcion)}|{precio}|{fecha}||");
        }

        private static void EscribirRegistroT(StreamWriter sw, Partida p)
        {
            if (string.IsNullOrWhiteSpace(p.TextoPliego)) return;
            sw.WriteLine($"~T|{p.Codigo}|{EscapeBc3(p.TextoPliego)}|");
        }

        private static void EscribirRegistroD_CapPartida(StreamWriter sw, MedicionPorPartida m)
        {
            var codCap = m.CapituloDestino.EndsWith("#") ? m.CapituloDestino : m.CapituloDestino + "##";
            // ~D|<PARE>|<FILL>\<FACTOR>\<RENDIMENT>\|  (factor=1, rendiment=1 per defecte)
            sw.WriteLine($"~D|{codCap}|{m.Partida.Codigo}\\1\\1\\|");
        }

        private static void EscribirRegistroM(StreamWriter sw, MedicionPorPartida m, IReadOnlyList<LineaMedicion> lineasEfectivas)
        {
            var codCap = m.CapituloDestino.EndsWith("#") ? m.CapituloDestino : m.CapituloDestino + "##";
            var totalEfectivo = lineasEfectivas.Sum(l => l.Cantidad);
            var total = totalEfectivo.ToString("0.###", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.Append($"~M|{codCap}\\{m.Partida.Codigo}|{total}|");

            foreach (var l in lineasEfectivas)
            {
                sb.Append(EscapeBc3(l.Comentario ?? ""));
                sb.Append('\\').Append(Dec(l.Unidades));
                sb.Append('\\').Append(Dec(l.Longitud));
                sb.Append('\\').Append(Dec(l.Latitud));
                sb.Append('\\').Append(Dec(l.Altura));
                sb.Append('\\');
            }
            sb.Append('|');
            sw.WriteLine(sb.ToString());
        }

        private static string Dec(decimal v)
        {
            if (v == 0m) return "";
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // Limpia caracteres conflictivos para BC3.
        private static string EscapeBc3(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace('|', ' ')
                .Replace('\\', '/')
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\r", " ");
        }
    }
}
