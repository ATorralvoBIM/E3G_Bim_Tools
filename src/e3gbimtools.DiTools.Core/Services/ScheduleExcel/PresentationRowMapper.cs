using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Averigua que elementos forman cada fila de una tabla «como en Revit», para
/// poder reimportar los cambios hechos sobre esa misma tabla en Excel.
///
/// La API no dice que elementos hay detras de cada fila. Se reconstruye:
/// una fila agrupa los elementos con los mismos valores en los campos de
/// ordenacion/agrupacion. Para cada fila de datos se buscan los elementos
/// cuyos valores coinciden con
///   • el texto de la columna, si el campo es visible, o
///   • el encabezado de grupo de su nivel, si el campo muestra encabezado,
/// y ademas con el resto de columnas visibles no vacias (Revit deja en blanco
/// las que varian). Los textos se comparan con el formato de la propia tabla.
///
/// Si dos filas comparten elementos, o en una tabla detallada hay varias filas
/// identicas, se marcan como ambiguas y no se importan.
/// </summary>
internal static class PresentationRowMapper
{
    public static void Map(ViewSchedule vs, ScheduleSnapshot snap, Action<double>? progress = null)
    {
        var doc = vs.Document;
        var def = vs.Definition;

        // Campos visibles = columnas del cuerpo, en orden
        var visible = new List<ScheduleField>();
        for (int i = 0; i < def.GetFieldCount(); i++)
        {
            var f = def.GetField(i);
            if (!f.IsHidden) visible.Add(f);
        }
        if (visible.Count != snap.ColumnCount) return;

        var elements = new FilteredElementCollector(doc, vs.Id)
            .WhereElementIsNotElementType()
            .ToElements();

        foreach (var f in visible)
        {
            var col = ScheduleEditableReader.DescribeField(doc, f, elements);
            // Un total (suma, min/max) no es el valor de un elemento
            if (f.DisplayType != ScheduleFieldDisplayType.Standard) col.Editable = false;
            snap.ColumnFields.Add(col);
        }
        if (elements.Count == 0) return;

        // Campos de ordenacion/agrupacion y nivel de encabezado de cada uno
        var sortFields = new List<(ScheduleField Field, int Column, int HeaderLevel)>();
        int level = 0;
        for (int i = 0; i < def.GetSortGroupFieldCount(); i++)
        {
            var sg = def.GetSortGroupField(i);
            var f = def.GetField(sg.FieldId);
            int header = sg.ShowHeader ? ++level : 0;
            if (!ScheduleEditableReader.IsParameterField(f)) continue;
            int column = visible.FindIndex(v => v.FieldId == f.FieldId);
            sortFields.Add((f, column, header));
        }

        // Columnas visibles que no son de ordenacion: filtro extra si la celda no esta vacia
        var otherColumns = Enumerable.Range(0, visible.Count)
            .Where(c => ScheduleEditableReader.IsParameterField(visible[c])
                        && visible[c].DisplayType == ScheduleFieldDisplayType.Standard
                        && sortFields.All(s => s.Column != c))
            .ToList();

        // Elementos agrupados por sus valores de ordenacion (texto como lo pinta la tabla)
        var buckets = new Dictionary<string, (string[] Raw, string[] NormText, List<Element> Items)>();
        int idx = 0;
        foreach (var e in elements)
        {
            if (++idx % 200 == 0) progress?.Invoke(0.5 * idx / elements.Count);
            var raw = sortFields.Select(s => ScheduleEditableReader.FieldText(doc, e, s.Field)).ToArray();
            var norm = raw.Select(Norm).ToArray();
            var key = string.Join("", norm);
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = (raw, norm, new List<Element>());
            bucket.Items.Add(e);
        }

        var otherCache = new Dictionary<(long, int), string>();
        string OtherText(Element e, int c)
        {
            if (!otherCache.TryGetValue((e.Id.Value, c), out var t))
                otherCache[(e.Id.Value, c)] = t = ScheduleEditableReader.FieldText(doc, e, visible[c]);
            return t;
        }

        var headers = new Dictionary<int, string>();
        bool itemized = def.IsItemized;
        var used = new HashSet<long>();

        int rowIdx = 0;
        foreach (var row in snap.Rows)
        {
            progress?.Invoke(0.5 + 0.5 * ++rowIdx / snap.Rows.Count);
            if (row.Kind == ScheduleRowKind.Group)
            {
                headers[row.Level] = row.Cells.FirstOrDefault()?.Text ?? "";
                foreach (var deeper in headers.Keys.Where(k => k > row.Level).ToList()) headers.Remove(deeper);
                continue;
            }

            string Cell(int c) => c >= 0 && c < row.Cells.Count && !row.Cells[c].Covered ? row.Cells[c].Text : "";

            var matches = new List<long>();
            foreach (var bucket in buckets.Values)
            {
                bool ok = true;
                for (int k = 0; k < sortFields.Count && ok; k++)
                {
                    var (_, column, headerLevel) = sortFields[k];
                    if (column >= 0) ok = Same(Cell(column), bucket.Raw[k], snap);
                    else if (headerLevel > 0 && headers.TryGetValue(headerLevel, out var h)) ok = HeaderMatches(h, bucket.Raw[k]);
                }
                if (!ok) continue;

                foreach (var e in bucket.Items)
                {
                    bool match = true;
                    foreach (var c in otherColumns)
                    {
                        var text = Cell(c);
                        if (text.Length > 0 && !Same(text, OtherText(e, c), snap)) { match = false; break; }
                    }
                    if (match) matches.Add(e.Id.Value);
                }
            }

            if (itemized)
            {
                var free = matches.Where(id => !used.Contains(id)).ToList();
                if (free.Count == 1) { used.Add(free[0]); row.ElementIds = free; }
                else if (free.Count > 1) { row.ElementIds = free; row.Ambiguous = true; }
            }
            else if (matches.Count > 0)
            {
                row.ElementIds = matches;
            }

            row.Label = string.Join(" › ",
                headers.OrderBy(h => h.Key).Select(h => h.Value).Where(t => t.Length > 0)
                    .Concat(row.Cells.Where(c => !c.Covered && c.Text.Length > 0).Take(2).Select(c => c.Text))
                    .Distinct());
        }

        // Filas que comparten elementos (encabezados sin campo visible, pies de grupo, totales)
        if (!itemized)
        {
            var owner = new Dictionary<long, ScheduleRow>();
            foreach (var row in snap.Rows.Where(r => r.ElementIds is not null))
            {
                foreach (var id in row.ElementIds!)
                {
                    if (owner.TryGetValue(id, out var other) && other != row)
                    {
                        other.Ambiguous = true;
                        row.Ambiguous = true;
                    }
                    else owner[id] = row;
                }
            }
        }
    }

    private static bool HeaderMatches(string header, string value)
    {
        var h = Norm(header);
        var v = Norm(value);
        return h == v || (v.Length > 0 && h.EndsWith(": " + v, StringComparison.Ordinal));
    }

    internal static bool Same(string a, string b, ScheduleSnapshot snap)
    {
        var na = Norm(a);
        var nb = Norm(b);
        if (na == nb) return true;
        return ScheduleExcelWriter.TryParseNumber(a, snap.DecimalSeparator, snap.GroupSeparator, out var va, out _, out var ua)
            && ScheduleExcelWriter.TryParseNumber(b, snap.DecimalSeparator, snap.GroupSeparator, out var vb, out _, out var ub)
            && string.Equals(ua ?? "", ub ?? "", StringComparison.OrdinalIgnoreCase)
            && Math.Abs(va - vb) <= 1e-9 * Math.Max(1, Math.Abs(va));
    }

    internal static string Norm(string? s) =>
        string.Join(" ", (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
