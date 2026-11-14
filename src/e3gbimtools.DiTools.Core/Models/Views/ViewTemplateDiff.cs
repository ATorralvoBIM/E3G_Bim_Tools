using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Views;

/// <summary>
/// Resultado de comparar dos View Templates (Sprint 4 - ViewManager).
/// Una lista plana de campos donde A y B difieren.  Si la lista esta vacia,
/// las plantillas son funcionalmente identicas.
/// </summary>
public sealed class ViewTemplateDiff
{
    public ViewTemplateDiff(
        ElementId templateAId, string templateAName,
        ElementId templateBId, string templateBName,
        IReadOnlyList<ViewTemplateFieldDiff> fieldDiffs)
    {
        TemplateAId = templateAId;
        TemplateAName = templateAName;
        TemplateBId = templateBId;
        TemplateBName = templateBName;
        FieldDiffs = fieldDiffs;
    }

    public ElementId TemplateAId { get; }
    public string TemplateAName { get; }
    public ElementId TemplateBId { get; }
    public string TemplateBName { get; }

    public IReadOnlyList<ViewTemplateFieldDiff> FieldDiffs { get; }

    public bool AreIdentical => FieldDiffs.Count == 0;
}

/// <summary>
/// Diferencia individual en un campo de la plantilla.
/// </summary>
public sealed class ViewTemplateFieldDiff
{
    public ViewTemplateFieldDiff(string section, string fieldName, string? valueA, string? valueB)
    {
        Section = section;
        FieldName = fieldName;
        ValueA = valueA;
        ValueB = valueB;
    }

    /// <summary>Agrupador UI: "VG Overrides", "General", "Filtros", etc.</summary>
    public string Section { get; }
    public string FieldName { get; }
    public string? ValueA { get; }
    public string? ValueB { get; }
}
