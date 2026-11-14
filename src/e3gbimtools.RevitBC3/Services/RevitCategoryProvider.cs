using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion de <see cref="IRevitCategoryProvider"/>.
/// Recorre el modelo una vez agrupando por categoria.
/// </summary>
public sealed class RevitCategoryProvider : IRevitCategoryProvider
{
    public IReadOnlyList<RevitCategoryInfo> GetCategoriesWithElements(Document doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var byBic = new Dictionary<BuiltInCategory, RevitCategoryInfo>();

        var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
        foreach (Element e in collector)
        {
            var cat = e.Category;
            if (cat is null) continue;
            var bic = cat.BuiltInCategory;
            if (bic == BuiltInCategory.INVALID) continue;

            // Solo categorias de MODELO (cosas medibles fisicamente).
            // Excluye Annotation (cotas, etiquetas), Internal (vistas, hojas,
            // plantillas), Analytical, AnalyticalModel.
            if (cat.CategoryType != CategoryType.Model) continue;

            // Excluye categorias que no representan elementos constructivos
            // aunque sean "Model" (ej. OST_Cameras, OST_SectionBox, OST_Levels).
            if (IsNonMeasurableModelCategory(bic)) continue;

            if (!byBic.TryGetValue(bic, out var info))
            {
                info = new RevitCategoryInfo
                {
                    BuiltInCategory = bic,
                    DisplayName = cat.Name ?? bic.ToString(),
                    ElementCount = 0,
                };
                byBic[bic] = info;
            }
            info.ElementCount++;
        }

        return byBic.Values
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Categorias de tipo Model que NO representan algo medible para
    /// presupuestar (camaras, lineas de seccion, niveles, ejes, etc.).
    /// </summary>
    private static bool IsNonMeasurableModelCategory(BuiltInCategory bic)
    {
        switch (bic)
        {
            // Lista conservadora: solo categorias que sabemos que existen en
            // la enum de Revit 2025+ y que claramente no son medibles.
            case BuiltInCategory.OST_Cameras:
            case BuiltInCategory.OST_SectionBox:
            case BuiltInCategory.OST_Levels:
            case BuiltInCategory.OST_Grids:
            case BuiltInCategory.OST_ReferencePoints:
            case BuiltInCategory.OST_Lines:
            case BuiltInCategory.OST_VolumeOfInterest:
            case BuiltInCategory.OST_Constraints:
            case BuiltInCategory.OST_ProjectInformation:
            case BuiltInCategory.OST_SiteProperty:
            case BuiltInCategory.OST_HVAC_Zones:
            case BuiltInCategory.OST_AreaSchemes:
            case BuiltInCategory.OST_RvtLinks:
            case BuiltInCategory.OST_DesignOptionSets:
            case BuiltInCategory.OST_DesignOptions:
            case BuiltInCategory.OST_RasterImages:
                return true;
            // OST_ReferencePlanes y OST_PartMaker fueron renombrados/eliminados
            // en Revit 2025+; el filtro CategoryType.Model ya los excluye.
            default:
                return false;
        }
    }
}
