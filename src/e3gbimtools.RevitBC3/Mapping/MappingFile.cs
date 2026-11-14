using System.Collections.Generic;

namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Estructura del archivo JSON de mapeo (rules.json).
/// Contiene las reglas y metadatos asociados (ej. ruta del ultimo BC3
/// cargado, para autocargarlo al reabrir la herramienta).
///
/// Historia de formatos:
///  - SchemaVersion 0 (Sprint A2 inicial):  array plano de MappingRule.
///  - SchemaVersion 1 (Sprint A2):  objeto con Rules + LastBC3Path.
///  - SchemaVersion 2 (Sprint B):   objeto con Chapters + SubChapters
///    como entidades de primera clase; las reglas referencian capitulo
///    y subcapitulo por FK (<see cref="MappingRule.ChapterId"/>,
///    <see cref="MappingRule.SubChapterId"/>).  Los campos string
///    legacy (ChapterCode/Name/SubChapterCode/Name) se conservan en
///    cada regla para migracion y export legacy bajo demanda.
///
/// La persistencia (<see cref="E3G.BimTools.RevitBC3.Services.MappingPersistenceService"/>)
/// hace la migracion automatica al cargar: detecta la version del fichero
/// y, si es la 0/1, crea Chapters/SubChapters derivados de los strings
/// unicos en las reglas y asigna las FKs.  Al guardar, escribe siempre
/// la version 2.
/// </summary>
public sealed class MappingFile
{
    /// <summary>
    /// Version del esquema (2 = Sprint B con Chapters/SubChapters como
    /// entidades de primera clase).
    /// </summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>
    /// Ruta absoluta del ultimo archivo BC3 que el usuario cargo con esta
    /// herramienta.  Si el archivo sigue existiendo, se autocarga al abrir
    /// la ventana.  Vacio si nunca se cargo un BC3 con este mapeo.
    /// </summary>
    public string LastBC3Path { get; set; } = string.Empty;

    /// <summary>
    /// Capitulos del presupuesto, en el orden persistido (Order).
    /// Se ignora si esta vacio y SchemaVersion &lt; 2 (formato antiguo).
    /// </summary>
    public List<Chapter> Chapters { get; set; } = new();

    /// <summary>
    /// Subcapitulos del presupuesto.  Cada uno apunta a su Chapter padre
    /// por ChapterId.
    /// </summary>
    public List<SubChapter> SubChapters { get; set; } = new();

    /// <summary>Reglas de mapeo Revit -> partida BC3.</summary>
    public List<MappingRule> Rules { get; set; } = new();
}
