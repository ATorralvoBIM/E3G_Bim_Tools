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

            EscribirRegistroV(sw, cabecera);
            EscribirRegistroK(sw);
            EscribirCapituloRaiz(sw, cabecera);
            EscribirCapitulosUtilizados(sw, mediciones, cabecera);

            foreach (var m in mediciones)
            {
                // Respetar exclusiones puntuales del drill-down.
                var lineasEfectivas = m.LineasParaExportar.ToList();
                if (lineasEfectivas.Count == 0) continue;

                EscribirRegistroC_Partida(sw, m.Partida, cabecera);
                EscribirRegistroT(sw, m.Partida);
                EscribirRegistroD_CapPartida(sw, m);
                EscribirRegistroM(sw, m, lineasEfectivas);
                sw.WriteLine();
            }

            sw.Flush();
        }

        // ─── ~V — Propiedad y versión ──────────────────────────────────────
        private static void EscribirRegistroV(StreamWriter sw, DatosCabecera c)
        {
            string fecha = c.Fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            sw.WriteLine(
                $"~V|{c.NombrePrograma}\\{c.Empresa}\\{c.Version}\\{fecha}\\{c.FormatoDeclarado}\\\\\\");
        }

        // ─── ~K — Coeficientes (decimales) ─────────────────────────────────
        private static void EscribirRegistroK(StreamWriter sw)
        {
            sw.WriteLine("~K|2\\2\\2\\3\\2\\2\\2\\3\\2\\\\|");
        }

        private static void EscribirCapituloRaiz(StreamWriter sw, DatosCabecera c)
        {
            string fecha = c.Fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            sw.WriteLine($"~C|{c.CodigoRaiz}|||{EscapeBc3(c.ProyectoNombre)}|0|{fecha}|");
        }

        private static void EscribirCapitulosUtilizados(
            StreamWriter sw,
            IReadOnlyList<MedicionPorPartida> mediciones,
            DatosCabecera c)
        {
            string fecha = c.Fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            var capitulos = mediciones
                .Where(m => !string.IsNullOrEmpty(m.CapituloDestino))
                .Select(m => m.CapituloDestino)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            foreach (var cap in capitulos)
            {
                var codCap = cap.EndsWith("#") ? cap : cap + "##";
                sw.WriteLine($"~C|{codCap}|||{EscapeBc3(NombreCapitulo(cap))}|0|{fecha}|");
                sw.WriteLine($"~D|{c.CodigoRaiz}|{codCap}\\1\\|");
            }
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
            string fecha = c.Fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            string precio = p.Precio.ToString("0.##", CultureInfo.InvariantCulture);
            sw.WriteLine(
                $"~C|{p.Codigo}|{p.Unidad}|EUR|{EscapeBc3(p.Descripcion)}|{precio}|{fecha}|");
        }

        private static void EscribirRegistroT(StreamWriter sw, Partida p)
        {
            if (string.IsNullOrWhiteSpace(p.TextoPliego)) return;
            sw.WriteLine($"~T|{p.Codigo}|{EscapeBc3(p.TextoPliego)}|");
        }

        private static void EscribirRegistroD_CapPartida(StreamWriter sw, MedicionPorPartida m)
        {
            var codCap = m.CapituloDestino.EndsWith("#") ? m.CapituloDestino : m.CapituloDestino + "##";
            sw.WriteLine($"~D|{codCap}|{m.Partida.Codigo}\\1\\|");
        }

        private static void EscribirRegistroM(StreamWriter sw, MedicionPorPartida m, IReadOnlyList<LineaMedicion> lineasEfectivas)
        {
            var codCap = m.CapituloDestino.EndsWith("#") ? m.CapituloDestino : m.CapituloDestino + "##";
            // El total tiene que reflejar lo que de verdad va al BC3 (sin las
            // líneas excluidas). Recalculamos a partir de lineasEfectivas.
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

        /// <summary>
        /// Limpia caracteres conflictivos para BC3.
        /// </summary>
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
