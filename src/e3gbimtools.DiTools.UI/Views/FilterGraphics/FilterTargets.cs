using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Reglas de "dónde se escribe" el grafismo de filtros de una vista.
///
/// Si la vista tiene plantilla y la plantilla CONTROLA "V/G Filtros"
/// (casilla Incluir marcada), Revit ignora lo que se escriba en la vista:
/// hay que escribir en la plantilla. Si la plantilla NO controla los filtros
/// (Incluir desmarcado), los filtros son de la propia vista.
/// </summary>
internal static class FilterTargets
{
    private static readonly ElementId FiltersParamId = new(BuiltInParameter.VIS_GRAPHICS_FILTERS);

    /// <summary>Vista donde realmente se aplica el grafismo de filtros de la vista activa.</summary>
    public static View? ResolveActive(Document doc)
        => doc.ActiveView is View active ? Effective(doc, active) : null;

    /// <summary>
    /// Para una vista cualquiera: su plantilla si ésta controla los filtros;
    /// si no, la propia vista. Una plantilla se devuelve tal cual.
    /// </summary>
    public static View Effective(Document doc, View view)
    {
        if (view.IsTemplate) return view;
        if (ControllingTemplate(doc, view) is { } tpl) return tpl;
        return view;
    }

    /// <summary>Plantilla que controla los filtros de la vista, o <c>null</c>.</summary>
    public static View? ControllingTemplate(Document doc, View view)
    {
        if (view.IsTemplate || view.ViewTemplateId == ElementId.InvalidElementId) return null;
        if (doc.GetElement(view.ViewTemplateId) is not View tpl) return null;
        return TemplateControlsFilters(tpl) ? tpl : null;
    }

    public static bool TemplateControlsFilters(View template)
    {
        try { return !template.GetNonControlledTemplateParameterIds().Contains(FiltersParamId); }
        catch { return true; }
    }

    /// <summary>Número de vistas (no plantillas) que usan esta plantilla.</summary>
    public static int CountViewsUsing(Document doc, View template)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Count(v => !v.IsTemplate && v.ViewTemplateId == template.Id);

    /// <summary>Texto de cabecera: destino y, si es plantilla, a cuántas vistas afecta.</summary>
    public static string Describe(Document doc, View target, out string warning)
    {
        warning = string.Empty;
        if (!target.IsTemplate) return $"Vista: {target.Name}";

        int n = CountViewsUsing(doc, target);
        warning = n switch
        {
            0 => "Ninguna vista usa esta plantilla.",
            1 => "Los cambios afectan a 1 vista que usa esta plantilla.",
            _ => $"Los cambios afectan a {n} vistas que usan esta plantilla.",
        };
        return $"Plantilla de vista: {target.Name}";
    }
}
