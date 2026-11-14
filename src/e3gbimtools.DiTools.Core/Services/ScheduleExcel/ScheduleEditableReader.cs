using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Lee una tabla de planificacion en formato editable: una fila por elemento
/// (los que la tabla muestra, con sus filtros) y una columna por campo visible
/// de ejemplar o de tipo. Los campos calculados, recuentos, de habitacion, etc.
/// no se pueden asignar a un parametro concreto y se omiten.
///
/// Los parametros se buscan comparando <see cref="Parameter.Id"/> con
/// <see cref="ScheduleField.ParameterId"/>, sin convertir a BuiltInParameter
/// (en Revit 2026 ese enum es Int64 y los casts fallan con la API 2025).
/// </summary>
internal static class ScheduleEditableReader
{
    public static ScheduleEditableData Read(ViewSchedule vs, Action<double>? progress = null)
    {
        var doc = vs.Document;
        var def = vs.Definition;
        var data = new ScheduleEditableData
        {
            Name = vs.Name,
            ScheduleUniqueId = vs.UniqueId,
            DocumentTitle = doc.Title,
        };

        var elements = new FilteredElementCollector(doc, vs.Id)
            .WhereElementIsNotElementType()
            .ToElements()
            .ToList();

        // ── Campos → columnas ──────────────────────────────────────────
        var fields = new List<ScheduleField>();
        for (int i = 0; i < def.GetFieldCount(); i++)
        {
            var f = def.GetField(i);
            if (f.IsHidden) continue;
            if (!IsParameterField(f)) { data.SkippedFields.Add(f.ColumnHeading); continue; }
            fields.Add(f);
        }

        foreach (var f in fields)
            data.Columns.Add(DescribeField(doc, f, elements));

        // ── Filas ──────────────────────────────────────────────────────
        int done = 0;
        foreach (var e in elements)
        {
            if (++done % 200 == 0) progress?.Invoke((double)done / elements.Count);
            var row = new EditableRow { ElementId = e.Id.Value };
            for (int i = 0; i < fields.Count; i++)
            {
                var col = data.Columns[i];
                var p = FindParameter(Owner(doc, e, col.IsType), fields[i].ParameterId);
                row.Values.Add(DisplayValue(doc, p, col));
            }
            data.Rows.Add(row);
        }

        Sort(def, fields, data);
        return data;
    }

    // ====================================================================
    // Parametros y valores
    // ====================================================================

    /// <summary>Campo ligado a un parametro de ejemplar o de tipo (no calculado, recuento, etc.).</summary>
    internal static bool IsParameterField(ScheduleField f) =>
        (f.FieldType == ScheduleFieldType.Instance || f.FieldType == ScheduleFieldType.ElementType)
        && f.ParameterId != ElementId.InvalidElementId
        && !f.IsCalculatedField;

    /// <summary>
    /// Describe la columna de un campo: tipo de valor, si es editable y las
    /// unidades con que la tabla lo muestra (las del campo si las sobrescribe,
    /// si no las del proyecto).
    /// </summary>
    internal static EditableColumn DescribeField(Document doc, ScheduleField f, IList<Element> elements)
    {
        bool isType = f.FieldType == ScheduleFieldType.ElementType;
        var col = new EditableColumn
        {
            Heading = string.IsNullOrWhiteSpace(f.ColumnHeading) ? f.GetName() : f.ColumnHeading,
            IsType = isType,
        };
        if (!IsParameterField(f)) { col.Kind = EditableKind.Text; col.Editable = false; return col; }

        col.ParameterId = f.ParameterId.Value;
        col.Key = f.ParameterId.Value.ToString(CultureInfo.InvariantCulture) + (isType ? ":T" : ":I");
        var sample = elements
            .Select(e => FindParameter(Owner(doc, e, isType), f.ParameterId))
            .FirstOrDefault(p => p is not null);
        Describe(col, sample, doc.GetUnits());

        if (col.Kind == EditableKind.Number)
        {
            try
            {
                var fo = f.GetFormatOptions();
                if (fo is not null && !fo.UseDefault)
                {
                    col.UnitTypeId = fo.GetUnitTypeId().TypeId;
                    col.Decimals = DecimalsFor(fo.Accuracy);
                }
            }
            catch { /* se quedan las unidades del proyecto */ }
        }
        return col;
    }

    /// <summary>Texto del valor de un campo para un elemento, como lo pinta la tabla.</summary>
    internal static string FieldText(Document doc, Element e, ScheduleField f)
    {
        var p = FindParameter(Owner(doc, e, f.FieldType == ScheduleFieldType.ElementType), f.ParameterId);
        if (p is null || !p.HasValue) return "";
        if (p.StorageType == StorageType.String) return p.AsString() ?? "";
        if (p.StorageType == StorageType.Double)
        {
            try
            {
                var fo = f.GetFormatOptions();
                if (fo is not null && !fo.UseDefault) return p.AsValueString(fo) ?? "";
            }
            catch { /* formato del proyecto */ }
        }
        return p.AsValueString() ?? "";
    }

    internal static Element? Owner(Document doc, Element e, bool isType)
    {
        if (!isType) return e;
        var typeId = e.GetTypeId();
        return typeId == ElementId.InvalidElementId ? null : doc.GetElement(typeId);
    }

    /// <summary>
    /// Parametro de un elemento por su Id. Ruta rapida: BuiltInParameter (id
    /// negativo, via Enum.ToObject: en Revit 2026 el enum es Int64) o
    /// definicion del ParameterElement (compartidos / de proyecto). Si la ruta
    /// rapida falla, se recorren los parametros (lento).
    /// </summary>
    internal static Parameter? FindParameter(Element? owner, ElementId parameterId)
    {
        if (owner is null) return null;
        long v = parameterId.Value;
        try
        {
            if (v < 0)
            {
                var bip = (BuiltInParameter)Enum.ToObject(typeof(BuiltInParameter), v);
                return owner.get_Parameter(bip);
            }
            switch (owner.Document.GetElement(parameterId))
            {
                case SharedParameterElement spe:
                    return owner.get_Parameter(spe.GuidValue);
                case ParameterElement pe:
                    return owner.get_Parameter(pe.GetDefinition());
            }
        }
        catch { /* ruta lenta */ }

        foreach (Parameter p in owner.Parameters)
            if (p.Id == parameterId) return p;
        return null;
    }

    private static void Describe(EditableColumn col, Parameter? p, Units units)
    {
        if (p is null) { col.Kind = EditableKind.Text; col.Editable = false; return; }

        col.Editable = !p.IsReadOnly;
        switch (p.StorageType)
        {
            case StorageType.String:
                col.Kind = EditableKind.Text;
                break;
            case StorageType.ElementId:
                col.Kind = EditableKind.ElementRef;
                col.Editable = false;
                break;
            case StorageType.Integer:
                col.Kind = IsYesNo(p) ? EditableKind.YesNo : EditableKind.Integer;
                break;
            case StorageType.Double:
                col.Kind = EditableKind.Number;
                try
                {
                    var spec = p.Definition.GetDataType();
                    col.SpecTypeId = spec.TypeId;
                    if (UnitUtils.IsMeasurableSpec(spec))
                    {
                        var fo = units.GetFormatOptions(spec);
                        col.UnitTypeId = fo.GetUnitTypeId().TypeId;
                        col.Decimals = DecimalsFor(fo.Accuracy);
                    }
                    else col.Decimals = 2;
                }
                catch { col.Decimals = 2; }
                break;
            default:
                col.Kind = EditableKind.Text;
                col.Editable = false;
                break;
        }
    }

    internal static bool IsYesNo(Parameter p)
    {
        try { return p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo; }
        catch { return false; }
    }

    private static int DecimalsFor(double accuracy)
    {
        if (accuracy <= 0 || accuracy >= 1) return 0;
        return Math.Min(6, (int)Math.Round(-Math.Log10(accuracy)));
    }

    /// <summary>Valor tal y como se escribe en Excel (unidades de visualizacion del proyecto).</summary>
    internal static object? DisplayValue(Document doc, Parameter? p, EditableColumn col)
    {
        if (p is null || !p.HasValue) return null;
        switch (col.Kind)
        {
            case EditableKind.Text:
                return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            case EditableKind.Integer:
                return (double)p.AsInteger();
            case EditableKind.YesNo:
                return p.AsInteger() != 0;
            case EditableKind.Number:
                double v = p.AsDouble();
                if (col.UnitTypeId is not null)
                {
                    try { v = UnitUtils.ConvertFromInternalUnits(v, new ForgeTypeId(col.UnitTypeId)); }
                    catch { /* se deja en unidades internas */ }
                }
                return Math.Round(v, 9);
            case EditableKind.ElementRef:
                var id = p.AsElementId();
                if (id == ElementId.InvalidElementId) return null;
                return doc.GetElement(id)?.Name ?? p.AsValueString();
            default:
                return p.AsValueString();
        }
    }

    // ====================================================================
    // Orden: el de los campos de ordenacion/agrupacion de la tabla
    // ====================================================================

    private static void Sort(ScheduleDefinition def, List<ScheduleField> fields, ScheduleEditableData data)
    {
        var keys = new List<(int Index, bool Desc)>();
        try
        {
            for (int i = 0; i < def.GetSortGroupFieldCount(); i++)
            {
                var sg = def.GetSortGroupField(i);
                int idx = fields.FindIndex(f => f.FieldId == sg.FieldId);
                if (idx >= 0) keys.Add((idx, sg.SortOrder == ScheduleSortOrder.Descending));
            }
        }
        catch { return; }
        if (keys.Count == 0) return;

        var sorted = data.Rows.ToList();
        sorted.Sort((a, b) =>
        {
            foreach (var (idx, desc) in keys)
            {
                int c = CompareValues(a.Values[idx], b.Values[idx]);
                if (c != 0) return desc ? -c : c;
            }
            return a.ElementId.CompareTo(b.ElementId);
        });
        data.Rows.Clear();
        data.Rows.AddRange(sorted);
    }

    private static int CompareValues(object? a, object? b)
    {
        if (a is null) return b is null ? 0 : -1;
        if (b is null) return 1;
        if (a is double da && b is double db) return da.CompareTo(db);
        return string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture),
                              Convert.ToString(b, CultureInfo.InvariantCulture),
                              StringComparison.CurrentCultureIgnoreCase);
    }
}
