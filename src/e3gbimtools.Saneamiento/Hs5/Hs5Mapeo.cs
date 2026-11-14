using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace E3G.Saneamiento.Hs5
{
    /// <summary>
    /// Mapeo "Familia|Tipo" → aparato de la tabla 4.1 (o MapeoHs5.Otro / Ignorar).
    /// Se guarda con la propia app, en Hs5Mapeo.json junto a la DLL del add-in: los
    /// aparatos son siempre los mismos y el mapeo vale para todos los proyectos.
    /// El fichero se versiona en el repo (Hs5\Hs5Mapeo.json) y el despliegue no
    /// sobrescribe el que ya está instalado.
    /// </summary>
    public static class Hs5Mapeo
    {
        public const string NombreFichero = "Hs5Mapeo.json";

        private static Dictionary<string, string> _mapeo;

        public static string Ruta => Path.Combine(
            Path.GetDirectoryName(typeof(Hs5Mapeo).Assembly.Location) ?? ".", NombreFichero);

        private static Dictionary<string, string> Mapeo => _mapeo ?? (_mapeo = Cargar());

        public static string De(string clave) => Mapeo.TryGetValue(clave, out var v) ? v : null;

        /// <summary>Asigna (o con null, quita) el mapeo de un tipo y lo guarda.</summary>
        public static void Asignar(string clave, string valor)
        {
            if (valor == null) Mapeo.Remove(clave);
            else Mapeo[clave] = valor;
            Guardar();
        }

        public static int Cantidad => Mapeo.Count;

        private static Dictionary<string, string> Cargar()
        {
            try
            {
                if (File.Exists(Ruta))
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Ruta))
                           ?? new Dictionary<string, string>();
            }
            catch { /* fichero corrupto: se empieza vacío */ }
            return new Dictionary<string, string>();
        }

        private static void Guardar()
        {
            try
            {
                var ordenado = new SortedDictionary<string, string>(Mapeo, StringComparer.OrdinalIgnoreCase);
                File.WriteAllText(Ruta, JsonSerializer.Serialize(ordenado, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* carpeta sin permisos: el mapeo vale para esta sesión */ }
        }
    }
}
