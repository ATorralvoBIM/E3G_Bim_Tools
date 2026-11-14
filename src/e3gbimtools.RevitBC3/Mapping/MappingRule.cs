using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;

namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Una regla de mapeo: une una categoria Revit (mas filtros opcionales)
/// con un codigo de partida BC3 y la medicion a calcular.
///
/// Ejemplo: "Tuberias con Sistema=AFS y Diametro=22 mm -> partida C04/IFD0001A,
/// medir por Longitud, factor 1.0, prioridad 100".
/// </summary>
public sealed class MappingRule
{
    /// <summary>Identificador unico (GUID estable para ediciones).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Nombre descriptivo de la regla, libre.  Opcional pero recomendado
    /// cuando hay multiples reglas con la misma partida (ej. distintas
    /// ubicaciones).  Si esta vacio, la tabla muestra el codigo BC3.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    // ============================================================
    //  Sprint B — Capitulo / Subcapitulo como entidades de primera
    //  clase.  Los campos string (ChapterCode/ChapterName/Sub*) se
    //  conservan SOLO para:
    //    1) Migracion: cargar mapping files antiguos.
    //    2) Export legacy: regenerar el formato antiguo bajo demanda.
    //  El resto del sistema (UI, writer, bimlink) usa las FKs.
    // ============================================================

    /// <summary>
    /// FK al <see cref="Chapter"/> al que pertenece la regla.  Null = regla
    /// "huerfana" (no asignada a capitulo todavia).  Las reglas huerfanas
    /// NO se exportan al BC3 (AC3.3 del Sprint B).
    /// </summary>
    public string? ChapterId { get; set; }

    /// <summary>
    /// FK al <see cref="SubChapter"/> al que pertenece la regla.  Null = la
    /// regla cuelga directamente del capitulo (sin subcapitulo intermedio).
    /// Debe ser hijo del Chapter referenciado por <see cref="ChapterId"/>.
    /// </summary>
    public string? SubChapterId { get; set; }

    /// <summary>
    /// [DEPRECATED Sprint B]  Codigo del capitulo.  Conservado para
    /// migracion de mapping files antiguos y para exportar en formato
    /// legacy.  En el modelo nuevo se deriva via FK a <see cref="Chapter"/>.
    /// </summary>
    public string ChapterCode { get; set; } = string.Empty;

    /// <summary>
    /// [DEPRECATED Sprint B]  Nombre del capitulo.  Conservado por las
    /// mismas razones que <see cref="ChapterCode"/>.
    /// </summary>
    public string ChapterName { get; set; } = string.Empty;

    /// <summary>
    /// [DEPRECATED Sprint B]  Codigo del subcapitulo.  Conservado por las
    /// mismas razones que <see cref="ChapterCode"/>.
    /// </summary>
    public string SubChapterCode { get; set; } = string.Empty;

    /// <summary>
    /// [DEPRECATED Sprint B]  Nombre del subcapitulo.  Conservado por las
    /// mismas razones que <see cref="ChapterCode"/>.
    /// </summary>
    public string SubChapterName { get; set; } = string.Empty;

    // ============================================================
    // Cache de datos del catalogo BC3 (issue #7).
    // ------------------------------------------------------------
    // Al asociar un codigo BC3 desde un catalogo importado, copiamos
    // su descripcion, unidad y precio aqui.  De este modo la regla
    // conserva esa informacion aunque el usuario reimporte otro BC3
    // que no contenga el mismo codigo.  Si el nuevo catalogo SI tiene
    // el codigo, los datos del nuevo prevalecen sobre estos.
    // ============================================================

    /// <summary>Resumen del concepto BC3 cacheado (campo ~C RESUMEN).</summary>
    public string CatalogSummary { get; set; } = string.Empty;

    /// <summary>Unidad de medida cacheada (m, m2, ud...).</summary>
    public string CatalogUnit { get; set; } = string.Empty;

    /// <summary>Precio unitario cacheado.</summary>
    public double CatalogPrice { get; set; }

    /// <summary>Descripcion extensa (texto ~T) cacheada (opcional).</summary>
    public string CatalogDescription { get; set; } = string.Empty;

    /// <summary>
    /// Categoria Revit como int (valor de la enum BuiltInCategory).
    /// Lo guardamos como int para que el JSON sea estable entre versiones
    /// y porque la enum no es serializable directamente con todas las versiones.
    /// </summary>
    public int CategoryValue { get; set; } = (int)BuiltInCategory.INVALID;

    /// <summary>Acceso tipado a la categoria.</summary>
    [JsonIgnore]
    public BuiltInCategory Category
    {
        get => (BuiltInCategory)CategoryValue;
        set => CategoryValue = (int)value;
    }

    /// <summary>Nombre legible de la categoria (localizado en el idioma de Revit del autor).</summary>
    public string CategoryDisplayName { get; set; } = string.Empty;

    /// <summary>Filtros que el elemento debe cumplir (AND logico).</summary>
    public List<MappingFilter> Filters { get; set; } = new();

    /// <summary>Codigo de la partida BC3 destino.</summary>
    public string BC3Code { get; set; } = string.Empty;

    /// <summary>Parametro de medicion (built-in o custom).</summary>
    public MeasurementParameter Measurement { get; set; } = new();

    /// <summary>
    /// Factor multiplicador aplicado a la medicion calculada antes de acumular.
    /// Util para conversiones de unidades o ajustes empiricos.  Default = 1.0.
    /// </summary>
    public double UnitFactor { get; set; } = 1.0;

    /// <summary>
    /// Prioridad cuando dos reglas se solapan (matchean el mismo elemento).
    /// Mayor numero = mayor prioridad.  Default = 100.
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>Si la regla esta habilitada (se evalua durante la extraccion).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Comentario libre (notas del usuario).</summary>
    public string Notes { get; set; } = string.Empty;

    // ============================================================
    // Propiedades calculadas / volatiles para la UI (no se serializan).
    // ============================================================

    /// <summary>Resumen de filtros para mostrar en la tabla.</summary>
    [JsonIgnore]
    public string FiltersSummary =>
        Filters.Count == 0 ? "(sin filtros)" : string.Join("  ·  ", Filters);

    /// <summary>Resumen del parametro de medicion para la tabla.</summary>
    [JsonIgnore]
    public string MeasurementSummary => Measurement?.ToString() ?? "—";

    /// <summary>
    /// True si el codigo BC3 existe en el documento BC3 cargado.
    /// Se actualiza desde fuera (cuando la herramienta valida las reglas
    /// contra el BC3 actual).  Default true cuando no hay BC3 cargado.
    /// </summary>
    [JsonIgnore]
    public bool CodeIsValid { get; set; } = true;

    /// <summary>
    /// True si esta regla colisiona con otra regla habilitada por compartir
    /// la misma terna (ChapterCode, SubChapterCode, BC3Code).  Lo recalcula
    /// <c>MappingRuleValidator</c> tras cada mutacion; default false.
    /// (Sprint A — Protocolo de avisos contra duplicados de partida.)
    /// </summary>
    [JsonIgnore]
    public bool HasConflict { get; set; }

    /// <summary>
    /// Tooltip explicando el conflicto detectado (vacio si no hay).
    /// Lo rellena <c>MappingRuleValidator</c>.  (Sprint A.)
    /// </summary>
    [JsonIgnore]
    public string ConflictTooltip { get; set; } = string.Empty;

    /// <summary>Texto/icono de estado para mostrar en la tabla.</summary>
    [JsonIgnore]
    public string ValidationStatus
    {
        get
        {
            // Prioridad de avisos: conflicto de duplicados > codigo no encontrado > OK.
            if (HasConflict) return "⚠ Duplicado de partida";
            if (!CodeIsValid) return "⚠ Codigo no encontrado";
            return "OK";
        }
    }

    /// <summary>Etiqueta legible: nombre si lo hay, si no la combinacion de categoria + partida.</summary>
    [JsonIgnore]
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name)
            ? $"{CategoryDisplayName} → {BC3Code}"
            : Name;

    /// <summary>Capitulo en formato "CODIGO - Nombre" o solo el que tenga valor.</summary>
    [JsonIgnore]
    public string ChapterDisplay
    {
        get
        {
            var c = (ChapterCode ?? string.Empty).Trim();
            var n = (ChapterName ?? string.Empty).Trim();
            if (c.Length == 0 && n.Length == 0) return "(sin capitulo)";
            if (c.Length == 0) return n;
            if (n.Length == 0) return c;
            return $"{c} — {n}";
        }
    }

    /// <summary>Subcapitulo en formato "CODIGO - Nombre" o solo el que tenga valor; vacio si no hay.</summary>
    [JsonIgnore]
    public string SubChapterDisplay
    {
        get
        {
            var c = (SubChapterCode ?? string.Empty).Trim();
            var n = (SubChapterName ?? string.Empty).Trim();
            if (c.Length == 0 && n.Length == 0) return string.Empty;
            if (c.Length == 0) return n;
            if (n.Length == 0) return c;
            return $"{c} — {n}";
        }
    }

    /// <summary>
    /// Camino completo Capitulo > Subcapitulo para agrupar visualmente.
    /// Si no hay subcapitulo devuelve solo el capitulo.
    /// </summary>
    [JsonIgnore]
    public string ChapterPathDisplay
    {
        get
        {
            var sub = SubChapterDisplay;
            return string.IsNullOrEmpty(sub)
                ? ChapterDisplay
                : $"{ChapterDisplay}  ▸  {sub}";
        }
    }

    public override string ToString() => $"{CategoryDisplayName} -> {BC3Code} (prio {Priority})";
}
