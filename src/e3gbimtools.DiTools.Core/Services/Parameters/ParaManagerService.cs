using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Filter;
using E3G.BimTools.DiTools.Core.Models.Parameters;

namespace E3G.BimTools.DiTools.Core.Services.Parameters;

/// <summary>
/// Implementacion del servicio principal de ParaManager.  Trabaja directamente
/// contra la API de Revit (BindingMap, DefinitionFile, Insert/Remove).  Toda
/// modificacion del BindingMap va envuelta en su propia transaccion porque la
/// API lo exige.
///
/// Las decisiones tecnicas mas importantes:
///  - Solo Revit 2025: usamos ForgeTypeId (SpecTypeId/GroupTypeId) directamente.
///  - El servicio NO crea el archivo de parametros compartidos por su cuenta:
///    si el documento no tiene uno, devolvemos warning y abortamos
///    (las operaciones de proyecto no-shared siguen funcionando).
///  - Filtramos parametros built-in de la lista (no se pueden editar/borrar).
/// </summary>
public sealed class ParaManagerService : IParaManagerService
{
    private readonly ITransactionService _tx;
    private readonly ILogger _logger;

    public ParaManagerService(ITransactionService tx, ILoggerFactory loggerFactory)
    {
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
        _logger = loggerFactory?.CreateLogger<ParaManagerService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // =====================================================================
    // Lectura
    // =====================================================================

    public IReadOnlyList<ProjectParameterInfo> ListProjectParameters(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var result = new List<ProjectParameterInfo>();
        var iterator = doc.ParameterBindings.ForwardIterator();
        iterator.Reset();

        while (iterator.MoveNext())
        {
            Definition def = iterator.Key;
            if (def is null) continue;

            ElementBinding? binding = iterator.Current as ElementBinding;
            if (binding is null) continue;

            var info = new ProjectParameterInfo
            {
                Name = def.Name ?? string.Empty,
                Scope = binding is InstanceBinding ? ParameterScope.Instance : ParameterScope.Type,
                GroupName = SafeGetGroupName(def),
                DataTypeName = SafeGetDataTypeName(def),
            };

            // Origen: shared (tiene Guid) o project.
            if (def is ExternalDefinition extDef)
            {
                info.Source = ParameterSource.Shared;
                try { info.SharedGuid = extDef.GUID; } catch { /* algunas versiones lanzan */ }
            }
            else
            {
                info.Source = ParameterSource.Project;
            }

            // Categorias vinculadas.
            foreach (Category cat in binding.Categories)
            {
                if (cat is null) continue;
                info.BoundCategoryNames.Add(cat.Name ?? string.Empty);
                try { info.BoundCategoryIds.Add(cat.Id.Value); } catch { /* sin id valido */ }
            }

            // DefinitionId (InternalDefinition.Id).
            if (def is InternalDefinition intDef)
            {
                try { info.DefinitionElementId = intDef.Id.Value; } catch { /* keep 0 */ }
            }

            result.Add(info);
        }

        return result
            .OrderBy(p => p.Source)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<CategoryDescriptor> GetBindableCategories(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var list = new List<CategoryDescriptor>();
        foreach (Category cat in doc.Settings.Categories)
        {
            if (cat is null) continue;
            if (string.IsNullOrWhiteSpace(cat.Name)) continue;
            if (!cat.AllowsBoundParameters) continue;

            BuiltInCategory bic;
            try { bic = (BuiltInCategory)cat.Id.Value; }
            catch { continue; }
            if (bic == BuiltInCategory.INVALID) continue;

            list.Add(new CategoryDescriptor
            {
                Name = cat.Name,
                BuiltInCategory = bic,
                CategoryId = cat.Id.Value,
            });
        }

        return list
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public SharedParameterFileInfo GetSharedParameterFile(Application app)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));

        var info = new SharedParameterFileInfo { Path = app.SharedParametersFilename ?? string.Empty };
        if (!info.IsAvailable) return info;
        if (!File.Exists(info.Path))
        {
            _logger.Warning("Shared parameter file configurado pero no existe en disco: {0}", info.Path);
            return info;
        }

        DefinitionFile? df;
        try { df = app.OpenSharedParameterFile(); }
        catch (Exception ex)
        {
            _logger.Warning("No se pudo abrir shared parameter file: {0}", ex.Message);
            return info;
        }
        if (df is null) return info;

        foreach (DefinitionGroup grp in df.Groups)
        {
            var g = new SharedParameterGroupInfo { Name = grp.Name ?? string.Empty };
            foreach (Definition d in grp.Definitions)
            {
                if (d is not ExternalDefinition extDef) continue;
                g.Definitions.Add(new SharedParameterDefinitionInfo
                {
                    Name = extDef.Name ?? string.Empty,
                    GroupName = grp.Name ?? string.Empty,
                    DataTypeName = SafeGetDataTypeName(extDef),
                    Guid = SafeGetGuid(extDef),
                });
            }
            info.Groups.Add(g);
        }
        return info;
    }

    public ParaManagerActionResult SetSharedParameterFile(Application app, string path)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new ParaManagerActionResult
            {
                Success = false,
                Summary = "El archivo indicado no existe.",
            };
        }

        try
        {
            app.SharedParametersFilename = path;
            return new ParaManagerActionResult { Success = true, Summary = "Archivo de parametros compartidos actualizado." };
        }
        catch (Exception ex)
        {
            return new ParaManagerActionResult { Success = false, Summary = ex.Message };
        }
    }

    // =====================================================================
    // Escritura: creacion
    // =====================================================================

    public ParaManagerActionResult CreateProjectParameter(Document doc, Application app, CreateProjectParameterRequest request)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (app is null) throw new ArgumentNullException(nameof(app));
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Name))
            return Fail("Nombre del parametro vacio.");
        if (request.Categories.Count == 0)
            return Fail("Debes seleccionar al menos una categoria.");

        // Para parametros COMPARTIDOS necesitamos un .txt configurado.
        ExternalDefinition? extDef = null;
        if (request.CreateAsShared)
        {
            extDef = EnsureSharedDefinition(app, request, out var sharedErr);
            if (extDef is null) return Fail(sharedErr ?? "No se pudo crear la definicion compartida.");
        }

        var catSet = app.Create.NewCategorySet();
        foreach (var bic in request.Categories)
        {
            try
            {
                var cat = doc.Settings.Categories.get_Item(bic);
                if (cat is not null && cat.AllowsBoundParameters)
                    catSet.Insert(cat);
            }
            catch { /* la categoria no soporta bindings: la descartamos silenciosamente */ }
        }
        if (catSet.IsEmpty)
            return Fail("Ninguna de las categorias seleccionadas admite parametros vinculables.");

        ForgeTypeId groupTypeId = ResolveGroupTypeId(request.PropertiesGroupName);
        ForgeTypeId specTypeId = ResolveSpecTypeId(request.DataType);

        var result = new ParaManagerActionResult();
        _tx.Run(doc, $"ParaManager: Crear parametro '{request.Name}'", d =>
        {
            try
            {
                Binding binding = request.Scope == ParameterScope.Instance
                    ? (Binding)app.Create.NewInstanceBinding(catSet)
                    : app.Create.NewTypeBinding(catSet);

                bool ok;
                if (request.CreateAsShared)
                {
                    ok = d.ParameterBindings.Insert(extDef!, binding, groupTypeId);
                }
                else
                {
                    // Parametro de PROYECTO puro: creamos una ExternalDefinition temporal,
                    // pero la API exige Definition.  Para "project parameter" sin shared
                    // file, hay que usar tambien una shared definition pero sin guardarla.
                    // En la practica, Revit no permite crear project parameters sin un
                    // .txt activo; si CreateAsShared=false avisamos al usuario.
                    result.Warnings.Add(
                        "Revit requiere un archivo .txt de parametros compartidos para crear parametros aunque no se guarden con GUID. " +
                        "Marca la opcion 'Crear como compartido' o configura un archivo en el entorno.");
                    ok = false;
                }

                if (!ok)
                {
                    result.Success = false;
                    result.Summary = "No se pudo vincular el parametro al documento.";
                    return;
                }

                result.Success = true;
                result.Summary = $"Parametro '{request.Name}' creado y vinculado a {request.Categories.Count} categoria(s).";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ParaManager Create failed");
                result.Success = false;
                result.Summary = ex.Message;
            }
        });

        return result;
    }

    public ParaManagerActionResult BindExistingSharedParameter(
        Document doc,
        Application app,
        SharedParameterDefinitionInfo definition,
        ParameterScope scope,
        string propertiesGroupName,
        IReadOnlyList<BuiltInCategory> categories)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (app is null) throw new ArgumentNullException(nameof(app));
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (categories is null || categories.Count == 0) return Fail("Selecciona al menos una categoria.");

        // Localizamos la ExternalDefinition real en el .txt.
        ExternalDefinition? extDef = FindSharedDefinition(app, definition.GroupName, definition.Name);
        if (extDef is null) return Fail($"No se encontro la definicion '{definition.Name}' en el archivo compartido.");

        var catSet = app.Create.NewCategorySet();
        foreach (var bic in categories)
        {
            try
            {
                var cat = doc.Settings.Categories.get_Item(bic);
                if (cat is not null && cat.AllowsBoundParameters)
                    catSet.Insert(cat);
            }
            catch { /* skip */ }
        }
        if (catSet.IsEmpty) return Fail("Ninguna categoria admite parametros vinculables.");

        ForgeTypeId groupTypeId = ResolveGroupTypeId(propertiesGroupName);
        var result = new ParaManagerActionResult();
        _tx.Run(doc, $"ParaManager: Vincular '{definition.Name}'", d =>
        {
            try
            {
                Binding binding = scope == ParameterScope.Instance
                    ? (Binding)app.Create.NewInstanceBinding(catSet)
                    : app.Create.NewTypeBinding(catSet);

                bool ok = d.ParameterBindings.Insert(extDef, binding, groupTypeId);
                if (!ok)
                {
                    // Insert falla si ya existe — probamos Re-Insert para actualizar el binding.
                    ok = d.ParameterBindings.ReInsert(extDef, binding, groupTypeId);
                }

                result.Success = ok;
                result.Summary = ok
                    ? $"'{definition.Name}' vinculado a {categories.Count} categoria(s)."
                    : "No se pudo vincular el parametro (puede que ya este vinculado con otro scope).";
            }
            catch (Exception ex) { result.Success = false; result.Summary = ex.Message; }
        });

        return result;
    }

    // =====================================================================
    // Escritura: actualizacion / borrado
    // =====================================================================

    public ParaManagerActionResult UpdateParameterBindings(
        Document doc,
        ProjectParameterInfo parameter,
        IReadOnlyList<BuiltInCategory> categories,
        ParameterScope scope)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (parameter is null) throw new ArgumentNullException(nameof(parameter));
        if (categories is null || categories.Count == 0) return Fail("Selecciona al menos una categoria.");

        var (def, _) = FindBoundDefinition(doc, parameter);
        if (def is null) return Fail($"No se localizo el parametro '{parameter.Name}' en el documento.");

        var app = doc.Application;
        var catSet = app.Create.NewCategorySet();
        foreach (var bic in categories)
        {
            try
            {
                var cat = doc.Settings.Categories.get_Item(bic);
                if (cat is not null && cat.AllowsBoundParameters) catSet.Insert(cat);
            }
            catch { /* skip */ }
        }
        if (catSet.IsEmpty) return Fail("Ninguna categoria admite parametros vinculables.");

        var groupTypeId = ResolveGroupTypeId(parameter.GroupName);
        var result = new ParaManagerActionResult();
        _tx.Run(doc, $"ParaManager: Recategorizar '{parameter.Name}'", d =>
        {
            try
            {
                Binding binding = scope == ParameterScope.Instance
                    ? (Binding)app.Create.NewInstanceBinding(catSet)
                    : app.Create.NewTypeBinding(catSet);

                bool ok = d.ParameterBindings.ReInsert(def, binding, groupTypeId);
                result.Success = ok;
                result.Summary = ok
                    ? $"Vinculaciones de '{parameter.Name}' actualizadas a {categories.Count} categoria(s)."
                    : "No se pudo actualizar el binding (Revit lo rechazo).";
            }
            catch (Exception ex) { result.Success = false; result.Summary = ex.Message; }
        });

        return result;
    }

    public ParaManagerActionResult DeleteProjectParameter(Document doc, ProjectParameterInfo parameter)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (parameter is null) throw new ArgumentNullException(nameof(parameter));

        var (def, intDef) = FindBoundDefinition(doc, parameter);
        if (def is null) return Fail($"No se localizo el parametro '{parameter.Name}' en el documento.");

        var result = new ParaManagerActionResult();
        _tx.Run(doc, $"ParaManager: Eliminar parametro '{parameter.Name}'", d =>
        {
            try
            {
                d.ParameterBindings.Remove(def);

                // Si el parametro es de proyecto (no shared) tambien borramos el InternalDefinition
                // para que desaparezca del modelo.
                if (intDef is not null && def is not ExternalDefinition)
                {
                    try { d.Delete(intDef.Id); } catch { /* algunos casos: solo Remove basta */ }
                }

                result.Success = true;
                result.Summary = $"Parametro '{parameter.Name}' eliminado del documento.";
            }
            catch (Exception ex) { result.Success = false; result.Summary = ex.Message; }
        });
        return result;
    }

    // =====================================================================
    // Mantenimiento del archivo de parametros compartidos
    // =====================================================================

    public ParaManagerActionResult AddGroupToSharedFile(Application app, string groupName)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));
        if (string.IsNullOrWhiteSpace(groupName)) return Fail("El nombre del grupo no puede estar vacio.");

        var df = app.OpenSharedParameterFile();
        if (df is null) return Fail("No hay archivo de parametros compartidos configurado.");

        try
        {
            var existing = df.Groups.Cast<DefinitionGroup>()
                .FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return new ParaManagerActionResult { Success = true, Summary = "El grupo ya existia." };

            df.Groups.Create(groupName);
            return new ParaManagerActionResult { Success = true, Summary = $"Grupo '{groupName}' creado." };
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    public ParaManagerActionResult AddDefinitionToSharedFile(
        Application app,
        string groupName,
        string definitionName,
        ParameterDataTypeOption dataType)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));
        if (string.IsNullOrWhiteSpace(groupName)) return Fail("El nombre del grupo no puede estar vacio.");
        if (string.IsNullOrWhiteSpace(definitionName)) return Fail("El nombre del parametro no puede estar vacio.");

        var df = app.OpenSharedParameterFile();
        if (df is null) return Fail("No hay archivo de parametros compartidos configurado.");

        try
        {
            var grp = df.Groups.Cast<DefinitionGroup>()
                          .FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase))
                      ?? df.Groups.Create(groupName);

            var existing = grp.Definitions.Cast<Definition>()
                .FirstOrDefault(d => string.Equals(d.Name, definitionName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return new ParaManagerActionResult { Success = true, Summary = "El parametro ya existia." };

            var opts = new ExternalDefinitionCreationOptions(definitionName, ResolveSpecTypeId(dataType));
            grp.Definitions.Create(opts);
            return new ParaManagerActionResult { Success = true, Summary = $"Parametro '{definitionName}' anadido al grupo '{groupName}'." };
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    // =====================================================================
    // Helpers privados
    // =====================================================================

    private static ParaManagerActionResult Fail(string msg) =>
        new() { Success = false, Summary = msg };

    private ExternalDefinition? EnsureSharedDefinition(Application app, CreateProjectParameterRequest req, out string? error)
    {
        error = null;
        var df = app.OpenSharedParameterFile();
        if (df is null)
        {
            error = "No hay archivo de parametros compartidos configurado en Revit. Configuralo en la pestaña 'Shared Parameter File' de ParaManager.";
            return null;
        }

        var grp = df.Groups.Cast<DefinitionGroup>()
                      .FirstOrDefault(g => string.Equals(g.Name, req.SharedGroupName, StringComparison.OrdinalIgnoreCase))
                  ?? df.Groups.Create(req.SharedGroupName);

        var existing = grp.Definitions.Cast<Definition>()
            .FirstOrDefault(d => string.Equals(d.Name, req.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is ExternalDefinition ext) return ext;

        var opts = new ExternalDefinitionCreationOptions(req.Name, ResolveSpecTypeId(req.DataType));
        return grp.Definitions.Create(opts) as ExternalDefinition;
    }

    private static ExternalDefinition? FindSharedDefinition(Application app, string groupName, string defName)
    {
        var df = app.OpenSharedParameterFile();
        if (df is null) return null;
        foreach (DefinitionGroup grp in df.Groups)
        {
            if (!string.Equals(grp.Name, groupName, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Definition d in grp.Definitions)
                if (d is ExternalDefinition ext && string.Equals(ext.Name, defName, StringComparison.OrdinalIgnoreCase))
                    return ext;
        }
        return null;
    }

    private static (Definition? def, InternalDefinition? intDef) FindBoundDefinition(Document doc, ProjectParameterInfo parameter)
    {
        var it = doc.ParameterBindings.ForwardIterator();
        it.Reset();
        while (it.MoveNext())
        {
            var d = it.Key;
            if (d is null) continue;
            if (!string.Equals(d.Name, parameter.Name, StringComparison.Ordinal)) continue;

            // Si tenemos GUID (shared), filtramos por GUID para evitar colision de nombre.
            if (parameter.SharedGuid.HasValue && d is ExternalDefinition ext)
            {
                Guid g; try { g = ext.GUID; } catch { continue; }
                if (g != parameter.SharedGuid.Value) continue;
            }

            return (d, d as InternalDefinition);
        }
        return (null, null);
    }

    // ---- ForgeTypeId mapping --------------------------------------------

    private static ForgeTypeId ResolveSpecTypeId(ParameterDataTypeOption dt) => dt switch
    {
        ParameterDataTypeOption.Text          => SpecTypeId.String.Text,
        ParameterDataTypeOption.MultilineText => SpecTypeId.String.MultilineText,
        ParameterDataTypeOption.Integer       => SpecTypeId.Int.Integer,
        ParameterDataTypeOption.Number        => SpecTypeId.Number,
        ParameterDataTypeOption.Length        => SpecTypeId.Length,
        ParameterDataTypeOption.Area          => SpecTypeId.Area,
        ParameterDataTypeOption.Volume        => SpecTypeId.Volume,
        ParameterDataTypeOption.Angle         => SpecTypeId.Angle,
        ParameterDataTypeOption.YesNo         => SpecTypeId.Boolean.YesNo,
        ParameterDataTypeOption.Material      => SpecTypeId.Reference.Material,
        ParameterDataTypeOption.Url           => SpecTypeId.String.Url,
        _                                     => SpecTypeId.String.Text,
    };

    private static ForgeTypeId ResolveGroupTypeId(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return GroupTypeId.Data;

        // Mapeo amable nombres comunes -> ForgeTypeId.  Lo demas cae a "Datos".
        var key = groupName.Trim().ToLowerInvariant();
        return key switch
        {
            "datos" or "data"                                 => GroupTypeId.Data,
            "datos de identidad" or "identity data"           => GroupTypeId.IdentityData,
            "texto" or "text"                                 => GroupTypeId.Text,
            "dimensiones" or "dimensions"                     => GroupTypeId.Geometry,
            "cotas" or "geometry"                             => GroupTypeId.Geometry,
            "materiales y acabados" or "materials"            => GroupTypeId.Materials,
            "fases" or "phasing"                              => GroupTypeId.Phasing,
            "graficos" or "graphics"                          => GroupTypeId.Graphics,
            "construccion" or "construction"                  => GroupTypeId.Construction,
            "cotas y restricciones" or "constraints"          => GroupTypeId.Constraints,
            "general" or "other"                              => GroupTypeId.General,
            _                                                 => GroupTypeId.Data,
        };
    }

    private static string SafeGetGroupName(Definition def)
    {
        try
        {
            if (def is InternalDefinition intDef)
            {
                var groupId = intDef.GetGroupTypeId();
                if (groupId is not null && !string.IsNullOrEmpty(groupId.TypeId))
                {
                    try { return LabelUtils.GetLabelForGroup(groupId); } catch { return groupId.TypeId; }
                }
            }
        }
        catch { /* fallback */ }
        return string.Empty;
    }

    private static string SafeGetDataTypeName(Definition def)
    {
        try
        {
            var spec = def.GetDataType();
            if (spec is null || string.IsNullOrEmpty(spec.TypeId)) return string.Empty;
            try { return LabelUtils.GetLabelForSpec(spec); } catch { return spec.TypeId; }
        }
        catch { return string.Empty; }
    }

    private static Guid SafeGetGuid(ExternalDefinition ext)
    {
        try { return ext.GUID; } catch { return Guid.Empty; }
    }
}
