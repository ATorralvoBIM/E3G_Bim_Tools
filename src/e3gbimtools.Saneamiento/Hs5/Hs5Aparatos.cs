using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

// =============================================================================
// CTE DB-HS5 — APARATOS SANITARIOS (tabla 4.1 / 4.2)
// -----------------------------------------------------------------------------
// Mapeo de tipos de aparato del modelo (familia + tipo) a filas de la tabla 4.1,
// y resolución de UD y diámetro mínimo de derivación según el uso. Sin Revit.
// =============================================================================
namespace E3G.Saneamiento.Hs5
{
    /// <summary>Valor especial de mapeo (además del nombre de un aparato de la tabla 4.1).</summary>
    public static class MapeoHs5
    {
        /// <summary>No evacúa a la red residual (p. ej. un termo sin desagüe): se ignora.</summary>
        public const string Ignorar = "__ignorar";
        /// <summary>Aparato no incluido en la tabla 4.1: UD por diámetro de desagüe (tabla 4.2).</summary>
        public const string Otro = "__otro";
    }

    /// <summary>Aparato conectado a la red, ya resuelto (UD y Ø exigidos).</summary>
    public sealed class AparatoConectado
    {
        public long Id { get; set; }
        public string Clave { get; set; }
        public string Nombre { get; set; }
        public double UD { get; set; }
        public int DN { get; set; }
        public bool SinMapear { get; set; }
        public bool Ignorado { get; set; }
        /// <summary>Si la UD de la derivación debe ser ≥ la del aparato (no en "Otro", que va sólo por Ø).</summary>
        public bool ComprobarUD { get; set; } = true;
        /// <summary>Uso con el que se ha resuelto el Ø (deducido de la UD del aparato si la tiene).</summary>
        public bool UsoPublico { get; set; }
        /// <summary>Observación (uso no previsto en la tabla, sin mapear…), o null.</summary>
        public string Aviso { get; set; }
    }

    public static class Hs5Aparatos
    {
        /// <summary>Clave de mapeo de un tipo de aparato.</summary>
        public static string Clave(string familia, string tipo) => $"{familia}|{tipo}";

        public static AparatoHs5 PorNombre(string nombre) => Hs5Tablas.T4_1.FirstOrDefault(a => a.Nombre == nombre);

        /// <summary>
        /// Propuesta automática por palabras clave (castellano y catalán) en el nombre
        /// de familia y tipo. Devuelve el nombre de la fila de la tabla 4.1,
        /// MapeoHs5.Otro / Ignorar, o null si no se reconoce.
        /// </summary>
        public static string Detectar(string familia, string tipo)
        {
            string n = Normalizar($"{familia} {tipo}");
            bool Tiene(params string[] claves) => claves.Any(n.Contains);

            if (Tiene("INODOR", "WC", "VATER"))
                return Tiene("FLUX") ? "Inodoro · Con fluxómetro" : "Inodoro · Con cisterna";
            if (Tiene("URINAR", "MINGITORI"))
                return Tiene("PEDEST") ? "Urinario · Pedestal"
                     : Tiene("BATER") ? "Urinario · En batería"
                     : "Urinario · Suspendido";
            if (Tiene("LAVAVAJILL", "RENTAVAIXELL", "LAVAPLAT", "DISHWASH")) return "Lavavajillas";
            if (Tiene("LAVADORA", "RENTADORA", "WASHING")) return "Lavadora";
            if (Tiene("LAVADERO", "SAFAREIG", "PILA DE LAVAR")) return "Lavadero";
            if (Tiene("LAVABO", "LAVAMANOS", "RENTAMANS")) return "Lavabo";
            if (Tiene("BIDE")) return "Bidé";
            if (Tiene("BANERA", "BANYERA", "BATHTUB")) return "Bañera (con o sin ducha)";
            if (Tiene("DUCHA", "DUTXA", "PLATO", "SHOWER")) return "Ducha";
            if (Tiene("FREGADERO", "AIGUERA", "PICA", "SINK"))
                return Tiene("LABOR", "RESTAUR", "INDUSTR") ? "Fregadero · De laboratorio, restaurante, etc."
                                                           : "Fregadero · De cocina";
            if (Tiene("VERTEDERO", "ABOCADOR", "SLOP")) return "Vertedero";
            if (Tiene("BEBEDERO", "FUENTE", "FONT DE BEURE", "FONT BEURE", "DRINKING")) return "Fuente para beber";
            if (Tiene("SUMIDERO", "BUNERA", "INTERCEPTOR", "FLOOR DRAIN")) return "Sumidero sifónico";
            if (Tiene("TERMO", "CALENTADOR", "ESCALFADOR", "CONDENS", "FANCOIL", "SPLIT", "CLIMA", "BOMBA DE CALOR"))
                return MapeoHs5.Otro;
            return null;
        }

        /// <summary>
        /// UD y Ø exigidos para un aparato. <paramref name="mapeo"/> es el valor
        /// guardado (o null → detección automática). <paramref name="udAparato"/> es la
        /// UD que lleva la propia familia (NaN o ≤ 0 si no la tiene): si existe, manda
        /// sobre la tabla y de ella se deduce el uso (privado/público) para el Ø.
        /// </summary>
        public static AparatoConectado Resolver(long id, string familia, string tipo, string mapeo,
                                                bool usoPublico, int dnDesagueMm, double udAparato = double.NaN)
        {
            var res = new AparatoConectado { Id = id, Clave = Clave(familia, tipo), UsoPublico = usoPublico };
            bool tieneUD = !double.IsNaN(udAparato) && udAparato > 0;
            string m = mapeo ?? Detectar(familia, tipo);

            if (m == MapeoHs5.Ignorar)
            {
                res.Ignorado = true; res.Nombre = "Ignorado";
                return res;
            }
            if (m == MapeoHs5.Otro || m == null || PorNombre(m) == null)
            {
                // Tabla 4.2 por el Ø del desagüe del propio aparato.
                res.SinMapear = m == null || (m != MapeoHs5.Otro && PorNombre(m) == null);
                res.Nombre = res.SinMapear ? $"{familia} (sin mapear)" : $"{familia} (tabla 4.2)";
                // Sólo se exige el Ø; la UD no se comprueba en estos aparatos.
                res.UD = tieneUD ? udAparato : Hs5Tablas.UDPorDiametroDesague(dnDesagueMm > 0 ? dnDesagueMm : 32);
                res.DN = dnDesagueMm;
                res.ComprobarUD = false;
                if (res.SinMapear) res.Aviso = "aparato sin mapear: se aplica la tabla 4.2 por Ø de desagüe";
                else if (dnDesagueMm <= 0) res.Aviso = "Ø de desagüe desconocido";
                return res;
            }

            var a = PorNombre(m);
            res.Nombre = a.Nombre;
            if (tieneUD)
            {
                // El uso lo dice la UD de la familia: si coincide con la de un uso, se toma ese.
                bool esPriv = Math.Abs(udAparato - a.UDPrivado) < 1e-6;
                bool esPub = Math.Abs(udAparato - a.UDPublico) < 1e-6;
                if (esPub && !esPriv) usoPublico = true;
                else if (esPriv && !esPub) usoPublico = false;
                else if (!esPriv && !esPub)
                    res.Aviso = $"UD de la familia {udAparato.ToString("0.#", Hs5Tablas.Es)} no coincide con la tabla 4.1 " +
                                $"(privado {Txt(a.UDPrivado)} / público {Txt(a.UDPublico)})";
                res.UsoPublico = usoPublico;
            }
            double udUso = usoPublico ? a.UDPublico : a.UDPrivado;
            int dnUso = usoPublico ? a.DNPublico : a.DNPrivado;
            if (double.IsNaN(udUso) || dnUso == 0)
            {
                // La tabla no prevé el aparato en este uso: se toma el otro uso.
                double udOtro = usoPublico ? a.UDPrivado : a.UDPublico;
                int dnOtro = usoPublico ? a.DNPrivado : a.DNPublico;
                if (double.IsNaN(udUso)) udUso = udOtro;
                if (dnUso == 0) dnUso = dnOtro;
                res.Aviso ??= $"tabla 4.1 sin valor para uso {(usoPublico ? "público" : "privado")}: se toma el otro uso";
            }
            res.UD = tieneUD ? udAparato : double.IsNaN(udUso) ? 0 : udUso;
            res.DN = dnUso;
            return res;
        }

        private static string Txt(double v) => double.IsNaN(v) ? "–" : v.ToString("0.#", Hs5Tablas.Es);

        /// <summary>Opciones de mapeo en el orden del desplegable: (valor, texto).</summary>
        public static IEnumerable<(string Valor, string Texto)> Opciones()
        {
            yield return (MapeoHs5.Otro, "Otro · tabla 4.2 por Ø de desagüe");
            yield return (MapeoHs5.Ignorar, "Ignorar (no evacúa)");
            foreach (var a in Hs5Tablas.T4_1) yield return (a.Nombre, a.Nombre);
        }

        private static string Normalizar(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToUpperInvariant(c));
            return sb.ToString().Replace('_', ' ');
        }
    }
}
