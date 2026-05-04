// ============================================================================
//  Bc3Reader.cs
//  Lector BC3 que extrae sólo lo que P2 necesita: ~C (partidas) + ~T (pliegos).
//  Multi-catálogo: cada llamada genera un CatalogoPartidas con Id propio y
//  Etiqueta visible (legible) inferida del nombre de archivo o del ~V.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using E3G.Saneamiento.Presupuesto.Modelo;

namespace E3G.Saneamiento.Presupuesto.Bc3
{
    public static class Bc3Reader
    {
        /// <summary>
        /// Lee un BC3 y devuelve un nuevo CatalogoPartidas con Id propio.
        /// La Etiqueta se infiere del nombre de archivo (sin extensión) y se
        /// puede sobreescribir desde la UI (rename amistoso) tras la importación.
        /// </summary>
        public static CatalogoPartidas LeerCatalogo(string rutaArchivo, string etiquetaCustom = null)
        {
            if (!File.Exists(rutaArchivo))
                throw new FileNotFoundException("BC3 no trobat", rutaArchivo);

            var encoding = Bc3Encoding.Detectar(rutaArchivo);
            var partidas = new List<Partida>();
            var textosPliego = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string nombreCatalogo = Path.GetFileNameWithoutExtension(rutaArchivo);
            string origenSegun_V  = null;
            int registrosIgnorados = 0;

            using (var sr = new StreamReader(rutaArchivo, encoding))
            {
                var registroActual = new StringBuilder();
                string linea;
                while ((linea = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(linea))
                    {
                        ProcesarRegistro(registroActual.ToString(), partidas, textosPliego, ref nombreCatalogo, ref origenSegun_V, ref registrosIgnorados);
                        registroActual.Clear();
                        continue;
                    }
                    if (linea.StartsWith("~"))
                    {
                        ProcesarRegistro(registroActual.ToString(), partidas, textosPliego, ref nombreCatalogo, ref origenSegun_V, ref registrosIgnorados);
                        registroActual.Clear();
                        registroActual.Append(linea);
                    }
                    else
                    {
                        registroActual.Append(linea);
                    }
                }
                ProcesarRegistro(registroActual.ToString(), partidas, textosPliego, ref nombreCatalogo, ref origenSegun_V, ref registrosIgnorados);
            }

            // Asociar pliegos a partidas — copia inmutable.
            var origenLegible = origenSegun_V ?? "BC3 importat";
            for (int i = 0; i < partidas.Count; i++)
            {
                var p = partidas[i];
                var pliego = textosPliego.TryGetValue(p.Codigo, out var t) ? t : p.TextoPliego;
                partidas[i] = new Partida
                {
                    Codigo = p.Codigo,
                    Descripcion = p.Descripcion,
                    Unidad = p.Unidad,
                    Precio = p.Precio,
                    TextoPliego = pliego,
                    Origen = origenLegible
                };
            }

            if (registrosIgnorados > 0)
                Debug.WriteLine($"[Bc3Reader] {registrosIgnorados} registres ignorats per format desconegut o mal format.");

            var etiqueta = !string.IsNullOrWhiteSpace(etiquetaCustom)
                ? etiquetaCustom
                : InferirEtiqueta(nombreCatalogo, origenSegun_V);

            return new CatalogoPartidas
            {
                Id = Guid.NewGuid(),
                Etiqueta = etiqueta,
                Partidas = partidas,
                OrigenArchivo = rutaArchivo,
                NombreCatalogo = nombreCatalogo,
                FechaImportacion = DateTime.Now
            };
        }

        // ─── Etiqueta legible para la UI ────────────────────────────────────
        private static string InferirEtiqueta(string nombreArchivo, string origenV)
        {
            // Heurística simple: si el nombre o el ~V mencionan "BEDEC", etiquetamos así.
            // Si menciona "E3G", "PRECIO_CENTRO", etc., también. En el resto, el nombre.
            var combinado = ((nombreArchivo ?? "") + " " + (origenV ?? "")).ToUpperInvariant();

            if (combinado.Contains("BEDEC")) return "BEDEC";
            if (combinado.Contains("ITEC"))  return "BEDEC (ITeC)";
            if (combinado.Contains("CENTRO")) return "Precio Centro";
            if (combinado.Contains("CYPE"))   return "CYPE";
            if (combinado.Contains("E3G"))    return "Plantilla E3G";

            return string.IsNullOrWhiteSpace(nombreArchivo) ? "BC3 importat" : nombreArchivo;
        }

        // ─── Despacho de registros ──────────────────────────────────────────
        private static void ProcesarRegistro(
            string raw,
            List<Partida> partidas,
            Dictionary<string, string> textosPliego,
            ref string nombreCatalogo,
            ref string origenSegun_V,
            ref int ignorados)
        {
            if (string.IsNullOrEmpty(raw)) return;
            if (!raw.StartsWith("~")) { ignorados++; return; }
            if (raw.Length < 3)       { ignorados++; return; }

            var tipo = raw[1];
            switch (tipo)
            {
                case 'V':
                    var nombreYOrigen = ExtraerNombreYOrigen(raw, nombreCatalogo);
                    nombreCatalogo = nombreYOrigen.nombre;
                    origenSegun_V = nombreYOrigen.origen;
                    break;
                case 'C': ProcesarConcepto(raw, partidas); break;
                case 'T': ProcesarTexto(raw, textosPliego); break;
                default:  break;
            }
        }

        private static (string nombre, string origen) ExtraerNombreYOrigen(string raw, string fallbackNombre)
        {
            var subs = ExtraerCampos(raw);
            string nombre = subs.Count >= 2 && !string.IsNullOrWhiteSpace(subs[1])
                ? subs[1]
                : fallbackNombre;
            string origen = subs.Count >= 3 ? subs[2] : null;
            return (nombre, origen);
        }

        private static void ProcesarConcepto(string raw, List<Partida> destino)
        {
            var subs = ExtraerCampos(raw);
            if (subs.Count < 5) return;

            var codigo = subs[1]?.Trim();
            if (string.IsNullOrEmpty(codigo)) return;
            if (codigo.EndsWith("#")) return; // capítulos

            var unidad  = subs[2]?.Trim() ?? "";
            var resumen = subs.Count > 4 ? (subs[4] ?? "").Trim() : "";
            var precioStr = subs.Count > 5 ? subs[5]?.Trim() : null;

            decimal precio = 0m;
            if (!string.IsNullOrEmpty(precioStr))
                decimal.TryParse(precioStr, NumberStyles.Float, CultureInfo.InvariantCulture, out precio);

            destino.Add(new Partida
            {
                Codigo = codigo,
                Unidad = unidad,
                Descripcion = resumen,
                Precio = precio
            });
        }

        private static void ProcesarTexto(string raw, Dictionary<string, string> destino)
        {
            var subs = ExtraerCampos(raw);
            if (subs.Count < 3) return;
            var codigo = subs[1]?.Trim();
            var texto = subs[2] ?? "";
            if (!string.IsNullOrEmpty(codigo))
                destino[codigo] = texto;
        }

        private static List<string> ExtraerCampos(string raw)
        {
            return raw.Substring(2).Split('|').ToList();
        }
    }
}
