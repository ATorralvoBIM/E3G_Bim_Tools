using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Parameters;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Servicio principal del ParaManager (Sprint 3).  Gestiona los parametros de
/// proyecto y compartidos del documento: lista lo existente, crea, elimina,
/// recategoriza, y manipula el archivo de parametros compartidos.
///
/// Convencion: este servicio abre sus propias transacciones cuando es
/// estrictamente necesario para operaciones del BindingMap (la API de Revit
/// requiere transaccion para Insert/Remove en el binding).  Para otras
/// operaciones puramente de archivo (.txt shared parameters) no hay
/// transaccion.
/// </summary>
public interface IParaManagerService
{
    // ---- Lectura ----

    /// <summary>Lista de parametros vinculados al documento (project + shared).</summary>
    IReadOnlyList<ProjectParameterInfo> ListProjectParameters(Document doc);

    /// <summary>Lista las categorias disponibles para vincular parametros (filtra las internas).</summary>
    IReadOnlyList<Models.Filter.CategoryDescriptor> GetBindableCategories(Document doc);

    /// <summary>Devuelve el archivo .txt de parametros compartidos actualmente configurado.</summary>
    SharedParameterFileInfo GetSharedParameterFile(Application app);

    /// <summary>Cambia el archivo .txt de parametros compartidos del entorno Revit.</summary>
    ParaManagerActionResult SetSharedParameterFile(Application app, string path);

    // ---- Escritura ----

    /// <summary>Crea un parametro de proyecto o compartido y lo vincula a las categorias indicadas.</summary>
    ParaManagerActionResult CreateProjectParameter(Document doc, Application app, CreateProjectParameterRequest request);

    /// <summary>Vincula al documento un parametro compartido que YA existe en el .txt.</summary>
    ParaManagerActionResult BindExistingSharedParameter(
        Document doc,
        Application app,
        SharedParameterDefinitionInfo definition,
        ParameterScope scope,
        string propertiesGroupName,
        IReadOnlyList<BuiltInCategory> categories);

    /// <summary>Actualiza las categorias vinculadas a un parametro existente.</summary>
    ParaManagerActionResult UpdateParameterBindings(
        Document doc,
        ProjectParameterInfo parameter,
        IReadOnlyList<BuiltInCategory> categories,
        ParameterScope scope);

    /// <summary>Elimina del documento un parametro de proyecto o compartido (no toca el .txt).</summary>
    ParaManagerActionResult DeleteProjectParameter(Document doc, ProjectParameterInfo parameter);

    /// <summary>Crea un grupo nuevo en el archivo .txt de shared parameters.</summary>
    ParaManagerActionResult AddGroupToSharedFile(Application app, string groupName);

    /// <summary>Crea una definicion nueva en un grupo del .txt de shared parameters.</summary>
    ParaManagerActionResult AddDefinitionToSharedFile(
        Application app,
        string groupName,
        string definitionName,
        ParameterDataTypeOption dataType);
}
