using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace E3G.Saneamiento.Hs5
{
    /// <summary>Red a la que pertenece una tubería según su tipo de sistema.</summary>
    public enum RedHs5Tipo { Residual, Pluvial, Mixta, Ventilacion }

    /// <summary>
    /// Opciones de la verificación HS5 elegidas en el panel. Se persisten por
    /// usuario en %APPDATA%\E3G\Saneamiento\hs5.json.
    /// </summary>
    public sealed class Hs5Config
    {
        public const string TablaAutomatica = "auto";
        public const int ColumnaDelModelo = -1;
        public const int AlturaDelModelo = -1, AlturaHasta3 = 0, AlturaMasDe3 = 1;

        /// <summary>"auto" (tabla según el tipo de tramo) o Id de tabla ("4.3", "4.4", "4.5", "4.8", "4.9").</summary>
        public string TablaId { get; set; } = TablaAutomatica;

        /// <summary>Tablas por pendiente: índice de columna fijo, o ColumnaDelModelo.</summary>
        public int ColumnaPendiente { get; set; } = ColumnaDelModelo;

        /// <summary>Tabla 4.4: plantas que vierten según el modelo, o forzado a ≤ 3 / > 3.</summary>
        public int ModoAltura { get; set; } = AlturaDelModelo;

        // ── Parámetros del modelo ──
        /// <summary>Parámetro de tubería con las UD acumuladas (lo genera el usuario).</summary>
        public string ParametroUD { get; set; } = "MC Discharge Unit Sum";
        /// <summary>Parámetro de UD en los aparatos sanitarios (si falta, se usa el de tubería).</summary>
        public string ParametroUDAparato { get; set; } = "MC Discharge Unit";
        /// <summary>
        /// Pluviales: el parámetro de UD contiene en realidad la superficie de recogida (m²),
        /// asignada en los sumideros y acumulada en las tuberías.
        /// </summary>
        public bool SuperficiePluvialEnParametroUD { get; set; } = true;
        /// <summary>Parámetro de superficie (m²) cuando no se usa el de UD (y en colectores mixtos).</summary>
        public string ParametroSuperficie { get; set; } = "E3G_HS5_Superficie";
        /// <summary>Parámetros de longitud y anchura interiores de la arqueta.</summary>
        public string ParametroLargoArqueta { get; set; } = "Longitud";
        public string ParametroAnchoArqueta { get; set; } = "Anchura";

        // ── Criterios ──
        /// <summary>Uso de los aparatos (tabla 4.1): público o privado.</summary>
        public bool UsoPublico { get; set; } = false;
        /// <summary>Zona pluviométrica (A/B) e isoyeta del proyecto activo, leídas del .rvt (Hs5Proyecto).</summary>
        [JsonIgnore] public string ZonaProyecto { get; set; }
        [JsonIgnore] public int IsoyetaProyecto { get; set; }
        [JsonIgnore] public string NombreProyecto { get; set; }
        /// <summary>
        /// Régimen pluviométrico i (mm/h) del proyecto activo: tabla B.1 con su zona e isoyeta;
        /// 0 = sin definir. No se guarda por usuario: cada proyecto tiene el suyo.
        /// </summary>
        [JsonIgnore] public double IntensidadProyecto { get; set; } = 0;
        /// <summary>El diámetro no disminuye aguas abajo (4.1.1.1-4 / 3.3.1.2).</summary>
        public bool NoDisminuirAguasAbajo { get; set; } = true;

        // ── Sistemas (palabras clave en el tipo de sistema, separadas por comas) ──
        public string SistemasResiduales { get; set; } = "10_10, 10_30, Fecal, Gris, Grey, Residual";
        public string SistemasPluviales { get; set; } = "10_20, Pluvial";
        public string SistemasMixtos { get; set; } = "Mixt, Unitari";
        public string SistemasVentilacion { get; set; } = "Ventil";

        [JsonIgnore] public bool EsAutomatico => Hs5Tablas.PorId(TablaId) == null;
        /// <summary>Tabla elegida a mano; null en modo automático.</summary>
        [JsonIgnore] public TablaHs5 Tabla => Hs5Tablas.PorId(TablaId);
        [JsonIgnore] public bool IntensidadDefinida => IntensidadProyecto > 0;
        /// <summary>Intensidad i (mm/h) aplicada: la del proyecto, o 100 (base de las tablas) si no está definida.</summary>
        [JsonIgnore] public double Intensidad => IntensidadDefinida ? IntensidadProyecto : 100.0;
        /// <summary>f = i / 100 (4.2.2.2). Sin régimen definido, f = 1: no se reduce ningún diámetro.</summary>
        [JsonIgnore] public double FactorPluviometrico => Intensidad / 100.0;
        /// <summary>"zona A · isoyeta 50 → i = 155 mm/h · f = 1,55" o "sin definir (f = 1)".</summary>
        [JsonIgnore] public string DescripcionPluviometria => IntensidadDefinida
            ? (ZonaProyecto != null ? $"zona {ZonaProyecto} · isoyeta {IsoyetaProyecto} → " : "") +
              $"i = {Intensidad.ToString("0.#", Hs5Tablas.Es)} mm/h · f = {FactorPluviometrico.ToString("0.00", Hs5Tablas.Es)}"
            : "sin definir: se aplica 100 mm/h (f = 1)";

        /// <summary>Red de un tipo de sistema, o null si no es de saneamiento.</summary>
        public RedHs5Tipo? RedDe(string sistema)
        {
            if (string.IsNullOrEmpty(sistema)) return null;
            bool Coincide(string lista) => (lista ?? "")
                .Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)
                .Any(x => sistema.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
            // Orden: la ventilación y los mixtos antes, por si comparten prefijo con residuales.
            if (Coincide(SistemasVentilacion)) return RedHs5Tipo.Ventilacion;
            if (Coincide(SistemasMixtos)) return RedHs5Tipo.Mixta;
            if (Coincide(SistemasPluviales)) return RedHs5Tipo.Pluvial;
            if (Coincide(SistemasResiduales)) return RedHs5Tipo.Residual;
            return null;
        }

        // ── Persistencia ──
        private static readonly string Ruta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "E3G", "Saneamiento", "hs5.json");

        private static Hs5Config _actual;
        public static Hs5Config Actual => _actual ?? (_actual = Cargar());

        private static Hs5Config Cargar()
        {
            try
            {
                if (File.Exists(Ruta))
                    return JsonSerializer.Deserialize<Hs5Config>(File.ReadAllText(Ruta)) ?? new Hs5Config();
            }
            catch { /* fichero corrupto: se usan los valores por defecto */ }
            return new Hs5Config();
        }

        public void Guardar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
                File.WriteAllText(Ruta, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* no bloquear el análisis por un fallo de escritura */ }
        }
    }
}
