// ============================================================================
//  Bc3Encoding.cs
//  Detección y selección de la codificación correcta para BC3.
//  Por defecto el formato FIEBDC-3 usa Windows-1252 (CP1252).
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace E3G.Saneamiento.Presupuesto.Bc3
{
    public static class Bc3Encoding
    {
        public static Encoding PorDefecto => Encoding.GetEncoding(1252);

        /// <summary>
        /// Garantiza que la codepage 1252 esté registrada (.NET 5+ no la trae
        /// por defecto). Llamar al inicio de la app.
        /// </summary>
        public static void RegistrarCodepageProvider()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
            catch { /* ya registrado */ }
        }

        public static Encoding Detectar(string rutaArchivo)
        {
            RegistrarCodepageProvider();

            // 1. BOM UTF-8.
            using (var fs = File.OpenRead(rutaArchivo))
            {
                var bom = new byte[3];
                int leidos = fs.Read(bom, 0, 3);
                if (leidos == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                    return new UTF8Encoding(true);
            }

            // 2. UTF-8 estricto + leer ~V para subcampo 7 (juego de caracteres).
            try
            {
                using var sr = new StreamReader(rutaArchivo, new UTF8Encoding(false, true));
                var primeraLinea = sr.ReadLine() ?? "";
                if (primeraLinea.StartsWith("~V"))
                {
                    var subs = primeraLinea.Split('|');
                    if (subs.Length >= 8)
                    {
                        var juego = subs[7]?.Trim().ToUpperInvariant();
                        if (juego == "UTF-8" || juego == "UTF8") return new UTF8Encoding(false);
                        if (juego == "ANSI"  || juego == "1252") return Encoding.GetEncoding(1252);
                        if (juego == "850"   || juego == "OEM")  return Encoding.GetEncoding(850);
                    }
                    return new UTF8Encoding(false);
                }
            }
            catch (DecoderFallbackException) { /* no UTF-8 */ }
            catch (Exception) { /* fallback */ }

            return Encoding.GetEncoding(1252);
        }

        public static Encoding ParaEscritura() => Encoding.GetEncoding(1252);
    }
}
