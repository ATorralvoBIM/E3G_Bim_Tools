using System.Collections.Generic;
using E3G.BimTools.DiTools.Core.Models.Filter;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Persistencia de plantillas OneFilter.  Las plantillas se guardan como JSON
/// en <c>%APPDATA%\e3g\DiTools\FilterTemplates\&lt;nombre&gt;.json</c>.
/// </summary>
public interface IFilterTemplateService
{
    /// <summary>Lista los nombres de plantillas existentes (sin extension).</summary>
    IReadOnlyList<string> ListTemplates();

    /// <summary>Guarda la definicion como plantilla.  Sobrescribe si ya existe.</summary>
    void Save(string name, FilterDefinition definition);

    /// <summary>Carga una plantilla por nombre.  Devuelve null si no existe o esta corrupta.</summary>
    FilterDefinition? Load(string name);

    /// <summary>Elimina una plantilla.  Devuelve true si se borro algo.</summary>
    bool Delete(string name);

    /// <summary>Ruta absoluta de la carpeta donde se guardan las plantillas.</summary>
    string TemplatesFolder { get; }
}
