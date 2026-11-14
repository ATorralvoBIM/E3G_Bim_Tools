using E3G.BimTools.RevitBC3.Mapping;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Persistencia del archivo de mapeo (reglas + metadatos como
/// ruta del ultimo BC3 cargado).
/// </summary>
public interface IMappingPersistenceService
{
    /// <summary>Ruta por defecto donde se guarda el mapeo si no se indica otra.</summary>
    string DefaultPath { get; }

    /// <summary>Carga el archivo de mapeo.  Si no existe, devuelve un <see cref="MappingFile"/> vacio.</summary>
    MappingFile Load(string filePath);

    /// <summary>Guarda el archivo de mapeo (sobrescribe si existe).</summary>
    void Save(string filePath, MappingFile file);

    /// <summary>
    /// Guarda el mapeo en el formato LEGACY (SchemaVersion 1, sin Chapters
    /// como entidades).  Las reglas se serializan con sus campos string
    /// (ChapterCode/Name/SubChapterCode/Name) sincronizados desde las FKs.
    /// Util para compatibilidad temporal con versiones anteriores del modulo
    /// (Sprint B, AC6.2).
    /// </summary>
    void SaveLegacy(string filePath, MappingFile file);
}
