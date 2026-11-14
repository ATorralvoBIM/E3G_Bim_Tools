// ============================================================================
//  E3G_Saneamiento — Clasificación de sistemas de tubería (servicio transversal)
// ----------------------------------------------------------------------------
//  Propósito
//  ---------
//  Sustituye la lógica hardcodeada que identifica los sistemas de tubería por
//  el contenido de su nombre (p.ej. sysType.Contains("10_10")) por una tabla
//  de clasificación explícita que el usuario define una vez por proyecto en
//  Ajustes › General › Classificació de sistemes.
//
//  Cada PipingSystemType del .rvt activo se asigna a una de 10 categorías:
//
//      SinAsignar              — sin clasificar (ignorado por todos los paneles)
//      SaneamientoFecales      — consumido por Saneamiento › Fecales (10_10)
//      SaneamientoPluviales    — consumido por Saneamiento › Pluviales (10_20)
//      SaneamientoGrises       — consumido por Saneamiento › Grises (10_30)
//      FontaneriaAFS           — consumido por Fontanería › AFS (20_10)
//      FontaneriaACS           — consumido por Fontanería › ACS (20_20)
//      FontaneriaAR            — consumido por Fontanería › AR (20_30)
//      PCIRociadores           — consumido por PCI › Rociadores (futuro)
//      PCIBIES                 — consumido por PCI › BIES (futuro)
//      Ignorar                 — no medir nunca
//
//  Persistencia
//  ------------
//  La tabla se guarda en el .rvt mediante ExtensibleStorage, sobre un
//  DataStorage dedicado. Esto permite que la configuración viaje con el
//  proyecto y que cada proyecto pueda tener su propia nomenclatura.
//
//  La idea futura de un archivo de preset (.e3gpreset) externo queda fuera
//  del alcance de este servicio — pero se anticipa con el método
//  ExportarParaPreset() / ImportarDesdePreset() que serializa el mapa a un
//  string JSON-like agnóstico de ElementId.
//
//  Auto-sugerencia
//  ---------------
//  Al abrir Ajustes, AutoSugerir(doc) recorre los PipingSystemType del modelo
//  y propone una categoría para cada uno usando dos heurísticas en cascada:
//      1) Nombre del tipo (más específico)  — distingue Rociadores vs BIES,
//         AFS vs ACS, fecales vs pluviales por palabras clave multilenguaje.
//      2) MEPSystemClassification (fallback) — clasifica genéricamente por
//         la categoría que Revit asigna al sistema.
//  El resultado es una sugerencia editable. El usuario revisa y guarda.
//
//  Edge cases manejados
//  --------------------
//   * Sin Document activo                → Service.IsAvailable == false
//   * Modelo sin PipingSystemType        → AutoSugerir devuelve dict vacío
//   * Tipos nuevos añadidos al modelo    → Aparecen como SinAsignar al recargar
//   * Asignaciones huérfanas (id ya no
//     existe en el modelo)               → Purgadas en silencio al cargar
//
//  IMPORTANTE — Transacciones
//  --------------------------
//  Los métodos que ESCRIBEN en el documento (Guardar, GetOrCreateStorage al
//  crear nuevo) deben llamarse desde dentro de una Transaction abierta por el
//  caller. Los métodos de lectura (Leer, AutoSugerir, ObtenerSistemas,
//  EsCategoria) NO requieren transacción.
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Plumbing;

namespace E3G.Saneamiento
{
    // ---------------------------------------------------------------------
    //  Enum — Categorías de clasificación
    // ---------------------------------------------------------------------
    /// <summary>
    /// Categorías a las que un PipingSystemType puede pertenecer.
    /// Los valores enteros se persisten en ExtensibleStorage; NO cambiar
    /// los números de los valores existentes (rompería proyectos antiguos).
    /// Para añadir nuevas categorías en el futuro, asignar nuevos enteros
    /// y dejar los existentes intactos.
    /// </summary>
    public enum CategoriaSistema
    {
        SinAsignar           = 0,
        SaneamientoFecales   = 1,
        SaneamientoPluviales = 2,
        SaneamientoGrises    = 3,
        FontaneriaAFS        = 4,
        FontaneriaACS        = 5,
        FontaneriaAR         = 6,
        PCIRociadores        = 7,
        PCIBIES              = 8,
        Ignorar              = 9
    }

    /// <summary>
    /// Metadatos de una categoría (etiqueta visible y agrupación).
    /// El color NO vive en la categoría — lo lee la UI directamente del
    /// PipingSystemType.LineColor de cada sistema (ver FilaClasificacion).
    /// </summary>
    public sealed class CategoriaInfo
    {
        public CategoriaSistema Categoria { get; }
        public string Etiqueta { get; }   // texto a mostrar en la UI (Catalán por defecto)
        public string Grupo    { get; }   // "Saneament" / "Fontaneria" / "PCI" / ""

        public CategoriaInfo(CategoriaSistema cat, string etiq, string grupo)
        {
            Categoria = cat; Etiqueta = etiq; Grupo = grupo;
        }
    }

    /// <summary>
    /// Catálogo de las 10 categorías. Único punto de la verdad para la UI.
    /// </summary>
    public static class CategoriasCatalogo
    {
        public static readonly IReadOnlyList<CategoriaInfo> Todas = new List<CategoriaInfo>
        {
            new CategoriaInfo(CategoriaSistema.SinAsignar,           "Sense assignar",        ""),
            new CategoriaInfo(CategoriaSistema.SaneamientoFecales,   "Sanejament — Fecals",   "Sanejament"),
            new CategoriaInfo(CategoriaSistema.SaneamientoPluviales, "Sanejament — Pluvials", "Sanejament"),
            new CategoriaInfo(CategoriaSistema.SaneamientoGrises,    "Sanejament — Grises",   "Sanejament"),
            new CategoriaInfo(CategoriaSistema.FontaneriaAFS,        "Fontaneria — AFS",      "Fontaneria"),
            new CategoriaInfo(CategoriaSistema.FontaneriaACS,        "Fontaneria — ACS",      "Fontaneria"),
            new CategoriaInfo(CategoriaSistema.FontaneriaAR,         "Fontaneria — AR",       "Fontaneria"),
            new CategoriaInfo(CategoriaSistema.PCIRociadores,        "PCI — Ruixadors",       "PCI"),
            new CategoriaInfo(CategoriaSistema.PCIBIES,              "PCI — BIES",            "PCI"),
            new CategoriaInfo(CategoriaSistema.Ignorar,              "Ignorar",               "")
        };

        public static CategoriaInfo Get(CategoriaSistema cat)
            => Todas.FirstOrDefault(c => c.Categoria == cat) ?? Todas[0];
    }

    // ---------------------------------------------------------------------
    //  Schema — ExtensibleStorage
    // ---------------------------------------------------------------------
    /// <summary>
    /// Schema para persistir la clasificación en el .rvt. Singleton — el
    /// mismo schema se usa en todos los proyectos.
    /// </summary>
    internal static class ClasificacionSistemasSchema
    {
        // Generado una sola vez. NO REGENERAR — rompería compatibilidad con
        // proyectos ya guardados.
        public static readonly Guid SchemaGuid = new Guid("E3C0F1A2-1B3D-4E5F-A6B7-C8D9E0F1A2B3");

        public const string SchemaName     = "E3G_ClasificacionSistemas";
        public const string FieldVersion   = "Version";
        public const string FieldMap       = "Asignaciones";
        public const int    CurrentVersion = 1;

        private static Schema _cached;

        public static Schema GetOrCreate()
        {
            if (_cached != null) return _cached;

            // Si ya existe en el doc actual (o cualquier doc cargado), reutilizar
            _cached = Schema.Lookup(SchemaGuid);
            if (_cached != null) return _cached;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetVendorId("E3GBimTools"); // mínim 4 caràcters ASCII alfanumèrics
            sb.SetSchemaName(SchemaName);
            sb.SetDocumentation(
                "E3G BIM Tools — Clasificación de PipingSystemType del proyecto en " +
                "categorías funcionales (Saneamiento, Fontanería, PCI). Consumida por " +
                "los paneles de medición de instalaciones.");

            // Versión del schema, para futuras migraciones
            sb.AddSimpleField(FieldVersion, typeof(int))
              .SetDocumentation("Versión del esquema (incrementar al hacer cambios incompatibles).");

            // Mapa: ElementId del PipingSystemType → entero de CategoriaSistema
            sb.AddMapField(FieldMap, typeof(ElementId), typeof(int))
              .SetDocumentation("Asignaciones de tipo de sistema a categoría funcional.");

            _cached = sb.Finish();
            return _cached;
        }
    }

    // ---------------------------------------------------------------------
    //  Servicio — interfaz pública
    // ---------------------------------------------------------------------
    /// <summary>
    /// Servicio transversal consumido por los paneles Saneamiento, Fontanería
    /// y PCI para resolver "¿qué tipos de sistema pertenecen a esta categoría?".
    /// </summary>
    public interface IClasificacionSistemasService
    {
        /// <summary>True si hay un Document Revit válido sobre el que operar.</summary>
        bool IsAvailable(Document doc);

        /// <summary>
        /// Devuelve los ElementId de PipingSystemType clasificados en una categoría
        /// dada. Si no hay configuración guardada o el doc es inválido, devuelve vacío.
        /// </summary>
        IEnumerable<ElementId> ObtenerSistemas(Document doc, CategoriaSistema categoria);

        /// <summary>
        /// True si el tipo dado está clasificado en la categoría indicada.
        /// </summary>
        bool EsCategoria(Document doc, ElementId tipoSistemaId, CategoriaSistema categoria);

        /// <summary>
        /// Lee el mapa completo guardado en el .rvt (sin sugerencias). Purga
        /// silenciosamente los huérfanos (ids que ya no existen en el modelo).
        /// </summary>
        Dictionary<ElementId, CategoriaSistema> Leer(Document doc);

        /// <summary>
        /// Guarda el mapa completo en el .rvt. REQUIERE Transaction abierta.
        /// </summary>
        void Guardar(Document doc, Dictionary<ElementId, CategoriaSistema> asignaciones);

        /// <summary>
        /// Genera una sugerencia inicial para todos los PipingSystemType del modelo
        /// que no aparezcan ya en el mapa guardado (los ya guardados se respetan).
        /// Usa heurísticas de nombre + MEPSystemClassification.
        /// </summary>
        Dictionary<ElementId, CategoriaSistema> AutoSugerir(Document doc);

        /// <summary>
        /// Combina el mapa guardado con la auto-sugerencia para los tipos no clasificados.
        /// Es lo que la UI de Ajustes muestra cuando el usuario abre la sección.
        /// </summary>
        Dictionary<ElementId, CategoriaSistema> LeerConSugerencias(Document doc);

        /// <summary>
        /// Exporta el mapa actual a un formato portable (sin ElementIds), pensado
        /// para guardar/cargar perfiles entre proyectos. Cada entrada es
        /// (NombreDelTipo, Categoria). Implementación futura del .e3gpreset.
        /// </summary>
        IEnumerable<KeyValuePair<string, CategoriaSistema>> ExportarParaPreset(Document doc);

        /// <summary>
        /// Aplica un preset portable al doc actual, casando por nombre de tipo.
        /// Devuelve cuántos casaron y cuántos no se encontraron.
        /// </summary>
        (int aplicados, int noEncontrados) ImportarDesdePreset(
            Document doc,
            IEnumerable<KeyValuePair<string, CategoriaSistema>> preset,
            Dictionary<ElementId, CategoriaSistema> destino);
    }

    // ---------------------------------------------------------------------
    //  Servicio — implementación
    // ---------------------------------------------------------------------
    public sealed class ClasificacionSistemasService : IClasificacionSistemasService
    {
        // ----- API pública --------------------------------------------------

        public bool IsAvailable(Document doc) => doc != null && !doc.IsFamilyDocument;

        public IEnumerable<ElementId> ObtenerSistemas(Document doc, CategoriaSistema categoria)
        {
            if (!IsAvailable(doc)) yield break;
            var mapa = Leer(doc);
            foreach (var kv in mapa)
                if (kv.Value == categoria)
                    yield return kv.Key;
        }

        public bool EsCategoria(Document doc, ElementId tipoSistemaId, CategoriaSistema categoria)
        {
            if (!IsAvailable(doc) || tipoSistemaId == null || tipoSistemaId == ElementId.InvalidElementId)
                return false;
            var mapa = Leer(doc);
            return mapa.TryGetValue(tipoSistemaId, out var cat) && cat == categoria;
        }

        public Dictionary<ElementId, CategoriaSistema> Leer(Document doc)
        {
            var resultado = new Dictionary<ElementId, CategoriaSistema>();
            if (!IsAvailable(doc)) return resultado;

            var schema = ClasificacionSistemasSchema.GetOrCreate();
            var storage = FindStorage(doc, schema);
            if (storage == null) return resultado;

            var entity = storage.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return resultado;

            IDictionary<ElementId, int> raw;
            try
            {
                raw = entity.Get<IDictionary<ElementId, int>>(ClasificacionSistemasSchema.FieldMap);
            }
            catch
            {
                return resultado; // schema corrupto o vacío
            }
            if (raw == null) return resultado;

            // Set de ids válidos en el modelo actual — para purgar huérfanos
            var idsValidos = new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystemType))
                    .ToElementIds());

            foreach (var kv in raw)
            {
                if (kv.Key == null || kv.Key == ElementId.InvalidElementId) continue;
                if (!idsValidos.Contains(kv.Key)) continue; // huérfano → silencio
                if (!Enum.IsDefined(typeof(CategoriaSistema), kv.Value)) continue; // valor desconocido
                resultado[kv.Key] = (CategoriaSistema)kv.Value;
            }
            return resultado;
        }

        public void Guardar(Document doc, Dictionary<ElementId, CategoriaSistema> asignaciones)
        {
            if (!IsAvailable(doc)) return;
            if (asignaciones == null) asignaciones = new Dictionary<ElementId, CategoriaSistema>();

            var schema  = ClasificacionSistemasSchema.GetOrCreate();
            var storage = FindStorage(doc, schema) ?? CreateStorage(doc);

            var entity = new Entity(schema);
            entity.Set(ClasificacionSistemasSchema.FieldVersion,
                       ClasificacionSistemasSchema.CurrentVersion);

            // Filtrar huérfanos antes de persistir
            var idsValidos = new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystemType))
                    .ToElementIds());

            IDictionary<ElementId, int> mapa = new Dictionary<ElementId, int>();
            foreach (var kv in asignaciones)
            {
                if (kv.Key == null || kv.Key == ElementId.InvalidElementId) continue;
                if (!idsValidos.Contains(kv.Key)) continue;
                mapa[kv.Key] = (int)kv.Value;
            }

            entity.Set(ClasificacionSistemasSchema.FieldMap, mapa);
            storage.SetEntity(entity);
        }

        public Dictionary<ElementId, CategoriaSistema> AutoSugerir(Document doc)
        {
            var sugerencias = new Dictionary<ElementId, CategoriaSistema>();
            if (!IsAvailable(doc)) return sugerencias;

            var tipos = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>();

            foreach (var t in tipos)
                sugerencias[t.Id] = SugerirCategoria(t);

            return sugerencias;
        }

        public Dictionary<ElementId, CategoriaSistema> LeerConSugerencias(Document doc)
        {
            var guardado = Leer(doc);
            var sugerencias = AutoSugerir(doc);
            // Las sugerencias rellenan los huecos; el guardado siempre prevalece
            foreach (var kv in sugerencias)
                if (!guardado.ContainsKey(kv.Key))
                    guardado[kv.Key] = kv.Value;
            return guardado;
        }

        public IEnumerable<KeyValuePair<string, CategoriaSistema>> ExportarParaPreset(Document doc)
        {
            if (!IsAvailable(doc)) yield break;
            var mapa = Leer(doc);
            foreach (var kv in mapa)
            {
                var tipo = doc.GetElement(kv.Key) as PipingSystemType;
                if (tipo == null) continue;
                yield return new KeyValuePair<string, CategoriaSistema>(tipo.Name ?? "", kv.Value);
            }
        }

        public (int aplicados, int noEncontrados) ImportarDesdePreset(
            Document doc,
            IEnumerable<KeyValuePair<string, CategoriaSistema>> preset,
            Dictionary<ElementId, CategoriaSistema> destino)
        {
            if (!IsAvailable(doc) || preset == null || destino == null) return (0, 0);

            var tiposPorNombre = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .GroupBy(t => t.Name ?? "")
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            int aplicados = 0, noEncontrados = 0;
            foreach (var kv in preset)
            {
                if (tiposPorNombre.TryGetValue(kv.Key ?? "", out var id))
                {
                    destino[id] = kv.Value;
                    aplicados++;
                }
                else
                {
                    noEncontrados++;
                }
            }
            return (aplicados, noEncontrados);
        }

        // ----- Privado: heurística de auto-sugerencia ----------------------

        private static CategoriaSistema SugerirCategoria(PipingSystemType t)
        {
            var nombre = (t?.Name ?? "").ToUpperInvariant();
            var clas   = t?.SystemClassification ?? MEPSystemClassification.UndefinedSystemClassification;

            // 1) Heurística por NOMBRE (más específica) — palabras clave multi-idioma.
            //    Las distinciones que MEPSystemClassification no puede hacer:
            //      Rociadores vs BIES, AFS vs ACS, Fecales vs Pluviales vs Grises.
            if (Contiene(nombre, "BIE", "BIES", "HIDRANT", "HIDRANTE", "MANGUERA"))
                return CategoriaSistema.PCIBIES;

            if (Contiene(nombre, "ROCIA", "RUIXAD", "SPRINK", "EXTINCION"))
                return CategoriaSistema.PCIRociadores;

            if (Contiene(nombre, "RETORN", "RETORNO", "RECIRC"))
                return CategoriaSistema.FontaneriaAR;

            if (Contiene(nombre, "ACS", "HOT", "CALENT", "CALIENTE"))
                return CategoriaSistema.FontaneriaACS;

            if (Contiene(nombre, "AFS", "COLD", "FRED", "FRIA", "AFCH"))
                return CategoriaSistema.FontaneriaAFS;

            if (Contiene(nombre, "PLUVIAL", "PLUVIALS", "STORM", "RAIN"))
                return CategoriaSistema.SaneamientoPluviales;

            if (Contiene(nombre, "GRIS", "GREY", "GRAY"))
                return CategoriaSistema.SaneamientoGrises;

            if (Contiene(nombre, "FECAL", "RESIDU", "WASTE", "SOIL", "NEGRA"))
                return CategoriaSistema.SaneamientoFecales;

            // Códigos numéricos típicos de E3G (legacy) — sólo si el nombre no dijo nada antes
            if (nombre.Contains("10_10")) return CategoriaSistema.SaneamientoFecales;
            if (nombre.Contains("10_20")) return CategoriaSistema.SaneamientoPluviales;
            if (nombre.Contains("10_30")) return CategoriaSistema.SaneamientoGrises;
            if (nombre.Contains("20_10")) return CategoriaSistema.FontaneriaAFS;
            if (nombre.Contains("20_20")) return CategoriaSistema.FontaneriaACS;
            if (nombre.Contains("20_30")) return CategoriaSistema.FontaneriaAR;

            // 2) Fallback por MEPSystemClassification
            switch (clas)
            {
                case MEPSystemClassification.Sanitary:
                    return CategoriaSistema.SaneamientoFecales;;
                case MEPSystemClassification.DomesticHotWater:
                    return CategoriaSistema.FontaneriaACS;
                case MEPSystemClassification.DomesticColdWater:
                    return CategoriaSistema.FontaneriaAFS;
                case MEPSystemClassification.FireProtectWet:
                case MEPSystemClassification.FireProtectDry:
                case MEPSystemClassification.FireProtectOther:
                case MEPSystemClassification.FireProtectPreaction:
                    // No podemos distinguir Rociadores vs BIES sin pista en el nombre
                    // → asumimos Rociadores (más común). El usuario corrige si es BIE.
                    return CategoriaSistema.PCIRociadores;
                default:
                    return CategoriaSistema.SinAsignar;
            }
        }

        private static bool Contiene(string texto, params string[] claves)
        {
            foreach (var k in claves)
                if (texto.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // ----- Privado: gestión del DataStorage ---------------------------

        private static DataStorage FindStorage(Document doc, Schema schema)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .WhereElementIsNotElementType()
                .Cast<DataStorage>()
                .FirstOrDefault(ds =>
                {
                    try
                    {
                        var e = ds.GetEntity(schema);
                        return e != null && e.IsValid();
                    }
                    catch { return false; }
                });
        }

        /// <summary>
        /// Crea un DataStorage nuevo. REQUIERE Transaction abierta por el caller.
        /// </summary>
        private static DataStorage CreateStorage(Document doc)
        {
            var ds = DataStorage.Create(doc);
            try { ds.Name = "E3G_ClasificacionSistemas_Storage"; }
            catch { /* nombre opcional, ignorar conflictos */ }
            return ds;
        }
    }

    // ---------------------------------------------------------------------
    //  Helper — vista para la UI (DataGrid de Ajustes)
    // ---------------------------------------------------------------------
    /// <summary>
    /// Fila de la tabla de Ajustes › Classificació de sistemes. Una por cada
    /// PipingSystemType del modelo. Los flags Nuevo/Sugerencia son cosméticos
    /// (badges en la UI) — no se persisten.
    /// </summary>
    public class FilaClasificacion
    {
        public ElementId TipoSistemaId { get; set; }
        public string    NombreTipo    { get; set; }
        public MEPSystemClassification ClasificacionRevit { get; set; }
        public CategoriaSistema CategoriaActual { get; set; }
        public bool EsSugerencia { get; set; }   // true = aún no guardado
        public bool EsNuevo      { get; set; }   // true = no estaba en el guardado anterior

        /// <summary>
        /// Color del PipingSystemType tal como está configurado en Revit
        /// (PipingSystemType.LineColor), formato "#RRGGBB".
        /// Si Revit no tiene color válido o es (0,0,0) ("by category"),
        /// devuelve un gris neutro de fallback (#94A3B8).
        /// </summary>
        public string ColorRevit { get; set; }

        public string NombreClasificacionRevit => ClasificacionRevit.ToString();
    }

    /// <summary>
    /// Construye el listado de filas que la UI de Ajustes pinta en su DataGrid.
    /// </summary>
    public static class FilaClasificacionBuilder
    {
        public static List<FilaClasificacion> Construir(
            Document doc,
            IClasificacionSistemasService servicio)
        {
            var filas = new List<FilaClasificacion>();
            if (doc == null || servicio == null || !servicio.IsAvailable(doc))
                return filas;

            var guardado    = servicio.Leer(doc);
            var sugerencias = servicio.AutoSugerir(doc);

            var tipos = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .OrderBy(t => t.Name);

            foreach (var t in tipos)
            {
                bool yaGuardado = guardado.TryGetValue(t.Id, out var catGuardada);
                CategoriaSistema cat = yaGuardado
                    ? catGuardada
                    : (sugerencias.TryGetValue(t.Id, out var sug) ? sug : CategoriaSistema.SinAsignar);

                filas.Add(new FilaClasificacion
                {
                    TipoSistemaId      = t.Id,
                    NombreTipo         = t.Name ?? "(sense nom)",
                    ClasificacionRevit = t.SystemClassification,
                    CategoriaActual    = cat,
                    EsSugerencia       = !yaGuardado,
                    EsNuevo            = !yaGuardado,
                    ColorRevit         = ExtraerColorRevit(t)
                });
            }
            return filas;
        }

        /// <summary>
        /// Convierte el LineColor del PipingSystemType en un hex "#RRGGBB"
        /// usable por el binding de WPF. Devuelve un gris neutro cuando
        /// el sistema no tiene color asignado o es (0,0,0) ("by category").
        /// </summary>
        private static string ExtraerColorRevit(PipingSystemType t)
        {
            const string fallback = "#94A3B8";   // gris neutro coherente con TextMuted
            if (t == null) return fallback;

            Color lc;
            try { lc = t.LineColor; }
            catch { return fallback; }

            if (lc == null || !lc.IsValid) return fallback;

            // Revit devuelve (0,0,0) tanto para color "by category" como para
            // negro real intencional. No podemos distinguirlos con la API,
            // así que tratamos negro puro como fallback (mejor un gris útil
            // que un punto invisible sobre el fondo claro de la UI).
            if (lc.Red == 0 && lc.Green == 0 && lc.Blue == 0) return fallback;

            return $"#{lc.Red:X2}{lc.Green:X2}{lc.Blue:X2}";
        }
    }
}
