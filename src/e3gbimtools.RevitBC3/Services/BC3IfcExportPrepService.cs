using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using E3G.BimTools.RevitBC3.Mapping;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion de <see cref="IBC3IfcExportPrepService"/>.
///
/// FLUJO:
///   1. Localizar o crear el archivo .spf en %APPDATA%\e3g\BC3\E3G_BC3_Params.spf.
///   2. Crear el grupo "E3G_Pset_BC3" y las 4 definiciones si no existen.
///   3. Vincular (bind) cada parametro a las categorias de los elementos matcheados.
///   4. En una sola transaccion, escribir los valores en cada elemento.
///
/// NOTA sobre nombres de PSET en IFC:
///   Revit exporta los parametros compartidos como propiedades IFC bajo un
///   PropertySet cuyo nombre depende del grupo de propiedades del parametro
///   (GroupTypeId.Data → "BaseQuantities" o "Pset_XXX" segun el tipo IFC).
///   Para forzar el nombre "E3G_Pset_BC3", se necesita un fichero de mapeo
///   de export IFC (UserDefinedPropertySets.txt). Aqui optamos por el
///   enfoque pragmatico: los parametros apareceran en el IFC con sus nombres
///   (E3G_BC3_Code, etc.) independientemente del nombre del PSET, y TCQ/Presto
///   los localiza por nombre de propiedad.
/// </summary>
public sealed class BC3IfcExportPrepService : IBC3IfcExportPrepService
{
    // ────────────────────────────────────────────────────────────────
    // Constantes
    // ────────────────────────────────────────────────────────────────

    private const string SpfGroupName  = "E3G_Pset_BC3";
    private const string ParamCode     = "E3G_BC3_Code";
    private const string ParamChapter  = "E3G_BC3_Chapter";
    private const string ParamQuantity = "E3G_BC3_Quantity";
    private const string ParamUnit     = "E3G_BC3_Unit";

    private static readonly string[] ParamNames =
        { ParamCode, ParamChapter, ParamQuantity, ParamUnit };

    // ────────────────────────────────────────────────────────────────
    // IBC3IfcExportPrepService
    // ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public BC3IfcExportPrepResult WriteToModel(
        Document doc,
        Application app,
        IReadOnlyList<MeasurementResult> measurements,
        IReadOnlyList<MappingRule> rules)
    {
        var result = new BC3IfcExportPrepResult();

        try
        {
            // ── 1. Indice de reglas por Id y por BC3Code ─────────────
            var rulesById = rules
                .Where(r => !string.IsNullOrEmpty(r.Id))
                .ToDictionary(r => r.Id, r => r, StringComparer.Ordinal);

            // ── 2. Construir mapa elementId → (code, chapter, qty, unit) ──
            var elementData = new Dictionary<long, ElementBc3Data>();

            foreach (var m in measurements.Where(m => !string.IsNullOrWhiteSpace(m.BC3Code) && m.FinalQuantity > 0))
            {
                MappingRule? rule = null;
                if (m.ContributingRuleIds is { Count: > 0 })
                {
                    foreach (var id in m.ContributingRuleIds)
                        if (rulesById.TryGetValue(id, out var r)) { rule = r; break; }
                }
                rule ??= rules.FirstOrDefault(r => r.Enabled && r.BC3Code == m.BC3Code);

                if (m.ElementContributions is null) continue;
                foreach (var c in m.ElementContributions.Where(c => c.Quantity > 0))
                {
                    // Si un elemento aparece en varias partidas, tomamos la primera
                    // (la de mayor prioridad, ya que el extractor las ordena por prioridad).
                    if (!elementData.ContainsKey(c.ElementId))
                    {
                        elementData[c.ElementId] = new ElementBc3Data
                        {
                            Code     = m.BC3Code,
                            Chapter  = rule?.ChapterCode?.Trim() ?? string.Empty,
                            Quantity = c.Quantity,
                            Unit     = m.Unit ?? string.Empty,
                        };
                    }
                }
            }

            if (elementData.Count == 0)
            {
                result.Success      = true;
                result.Warnings.Add("No hay elementos con medicion > 0. Calcula primero las mediciones.");
                return result;
            }

            // ── 3. Coleccionar categorias necesarias ─────────────────
            var categoryIds = new HashSet<ElementId>();
            foreach (var id in elementData.Keys)
            {
                var el = doc.GetElement(new ElementId((int)id));
                if (el?.Category is not null)
                    categoryIds.Add(el.Category.Id);
            }
            result.CategoriesProcessed = categoryIds.Count;

            // ── 4. Gestionar el archivo .spf ─────────────────────────
            var spfPath   = GetOrCreateSpfPath();
            var prevSpf   = app.SharedParametersFilename;
            var spfChanged = !string.Equals(prevSpf, spfPath, StringComparison.OrdinalIgnoreCase);

            try
            {
                if (spfChanged) app.SharedParametersFilename = spfPath;

                var defFile = app.OpenSharedParameterFile();
                if (defFile is null)
                {
                    result.Success = false;
                    result.ErrorMessage = $"No se pudo abrir el archivo de parametros compartidos: {spfPath}";
                    return result;
                }

                // Crear o recuperar el grupo
                var group = defFile.Groups.get_Item(SpfGroupName)
                         ?? defFile.Groups.Create(SpfGroupName);

                // Crear definiciones de texto (Code, Chapter, Unit) y numero (Quantity)
                // Usamos variable local porque ref no admite propiedades (CS0206).
                int created = 0;
                if (EnsureDefinition(group, ParamCode,     SpecTypeId.String.Text)) created++;
                if (EnsureDefinition(group, ParamChapter,  SpecTypeId.String.Text)) created++;
                if (EnsureDefinition(group, ParamQuantity, SpecTypeId.Number))      created++;
                if (EnsureDefinition(group, ParamUnit,     SpecTypeId.String.Text)) created++;
                result.ParametersCreated = created;

                // ── 5. Vincular parametros a las categorias necesarias ─
                BindParameters(doc, app, defFile, group, categoryIds);
            }
            finally
            {
                if (spfChanged) app.SharedParametersFilename = prevSpf;
            }

            // ── 6. Escribir valores en una sola transaccion ──────────
            using var tx = new Transaction(doc, "E3G: Escribir parametros BC3 para IFC");
            tx.Start();

            int updated = 0;
            foreach (var (id, data) in elementData)
            {
                var el = doc.GetElement(new ElementId((int)id));
                if (el is null) continue;

                var pCode     = el.LookupParameter(ParamCode);
                var pChapter  = el.LookupParameter(ParamChapter);
                var pQuantity = el.LookupParameter(ParamQuantity);
                var pUnit     = el.LookupParameter(ParamUnit);

                bool wrote = false;
                if (pCode     is not null && !pCode.IsReadOnly)    { pCode.Set(data.Code);           wrote = true; }
                if (pChapter  is not null && !pChapter.IsReadOnly)  pChapter.Set(data.Chapter);
                if (pQuantity is not null && !pQuantity.IsReadOnly) pQuantity.Set(data.Quantity);
                if (pUnit     is not null && !pUnit.IsReadOnly)     pUnit.Set(data.Unit);

                if (wrote) updated++;
            }

            tx.Commit();

            result.ElementsUpdated = updated;
            result.Success         = true;

            if (updated < elementData.Count)
                result.Warnings.Add(
                    $"{elementData.Count - updated} elemento(s) no pudieron actualizarse " +
                    "(parametros de solo lectura o categoria sin binding).");
        }
        catch (Exception ex)
        {
            result.Success       = false;
            result.ErrorMessage  = ex.Message;
        }

        return result;
    }

    /// <inheritdoc/>
    public BC3IfcExportPrepResult ClearFromModel(Document doc)
    {
        var result = new BC3IfcExportPrepResult();
        try
        {
            using var tx = new Transaction(doc, "E3G: Limpiar parametros BC3 del modelo");
            tx.Start();

            int cleared = 0;
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var el in collector)
            {
                bool any = false;
                foreach (var name in ParamNames)
                {
                    var p = el.LookupParameter(name);
                    if (p is null || p.IsReadOnly) continue;
                    if (p.StorageType == StorageType.String)  { p.Set(string.Empty); any = true; }
                    if (p.StorageType == StorageType.Double)  { p.Set(0.0);          any = true; }
                }
                if (any) cleared++;
            }

            tx.Commit();
            result.Success        = true;
            result.ElementsUpdated = cleared;
        }
        catch (Exception ex)
        {
            result.Success      = false;
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers privados
    // ────────────────────────────────────────────────────────────────

    private static string GetOrCreateSpfPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "e3g", "BC3");
        Directory.CreateDirectory(dir);

        var spfPath = Path.Combine(dir, "E3G_BC3_Params.spf");

        // Crear un .spf vacio si no existe (formato: cabecera estandar Revit SPF)
        if (!File.Exists(spfPath))
        {
            File.WriteAllText(spfPath,
                "# This is a Revit shared parameter file.\r\n" +
                "# Do not edit manually.\r\n" +
                "*META\tVERSION\tMINVERSION\r\n" +
                "META\t2\t1\r\n" +
                "*GROUP\tID\tNAME\r\n" +
                "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODABLE\tHIDEWHEN_NO_VALUE\r\n",
                System.Text.Encoding.UTF8);
        }
        return spfPath;
    }

    /// <returns>True si la definicion se creo nueva; false si ya existia.</returns>
    private static bool EnsureDefinition(
        DefinitionGroup group,
        string paramName,
        ForgeTypeId specTypeId)
    {
        if (group.Definitions.get_Item(paramName) is not null) return false;

        var opts = new ExternalDefinitionCreationOptions(paramName, specTypeId)
        {
            Visible        = true,
            UserModifiable = true,
        };
        group.Definitions.Create(opts);
        return true;
    }

    private static void BindParameters(
        Document doc,
        Application app,
        DefinitionFile defFile,
        DefinitionGroup group,
        IEnumerable<ElementId> categoryIds)
    {
        // Construir CategorySet con las categorias de los elementos matcheados
        var catSet = app.Create.NewCategorySet();
        foreach (var catId in categoryIds)
        {
            var cat = Category.GetCategory(doc, catId);
            if (cat is not null) catSet.Insert(cat);
        }
        if (catSet.IsEmpty) return;

        var binding = app.Create.NewInstanceBinding(catSet);
        var map     = doc.ParameterBindings;

        using var tx = new Transaction(doc, "E3G: Vincular parametros BC3 a categorias");
        tx.Start();

        foreach (var paramName in ParamNames)
        {
            var def = group.Definitions.get_Item(paramName) as ExternalDefinition;
            if (def is null) continue;

            // Si ya esta vinculado, ampliar el CategorySet; si no, insertar.
            if (map.Contains(def))
                map.ReInsert(def, binding, GroupTypeId.Data);
            else
                map.Insert(def, binding, GroupTypeId.Data);
        }

        tx.Commit();
    }

    /// <summary>DTO interno para los datos BC3 de un elemento.</summary>
    private sealed class ElementBc3Data
    {
        public string Code     { get; set; } = string.Empty;
        public string Chapter  { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public string Unit     { get; set; } = string.Empty;
    }
}
