using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Compara un Excel editable con el modelo (vista previa de cambios) y aplica
/// los cambios marcados en una unica transaccion: un Ctrl+Z lo deshace todo.
/// </summary>
internal static class ScheduleImporter
{
    private static readonly string[] TrueWords = { "sí", "si", "s", "yes", "y", "true", "verdadero", "1", "x" };
    private static readonly string[] FalseWords = { "no", "n", "false", "falso", "0", "" };

    // ====================================================================
    // Vista previa
    // ====================================================================

    public static ScheduleImportPreview Preview(Document doc, ParsedEditableFile file, Action<double>? progress = null)
    {
        var preview = new ScheduleImportPreview();
        var units = doc.GetUnits();
        int total = Math.Max(1, file.Sheets.Sum(s => s.Rows.Count) + file.PresentationSheetsData.Sum(s => s.Rows.Count));
        int done = 0;
        void Step() { if (++done % 10 == 0) progress?.Invoke((double)done / total); }

        foreach (var sheet in file.Sheets)
        {
            var name = sheet.ScheduleName ?? sheet.SheetName;
            preview.ScheduleNames.Add(name);
            preview.Warnings.AddRange(sheet.Warnings);
            preview.ExportedAt ??= sheet.ExportedAt;
            preview.RowsRead += sheet.Rows.Count;

            if (!string.IsNullOrEmpty(sheet.DocumentTitle) &&
                !string.Equals(sheet.DocumentTitle, doc.Title, StringComparison.OrdinalIgnoreCase))
                preview.DocumentMatches = false;
            if (!string.IsNullOrEmpty(sheet.ScheduleUniqueId) && doc.GetElement(sheet.ScheduleUniqueId) is not ViewSchedule)
                preview.Warnings.Add("La tabla «" + name + "» ya no existe en el modelo; se importa igualmente por ElementId.");

            foreach (var row in sheet.Rows)
            {
                Step();
                var el = doc.GetElement(new ElementId(row.ElementId));
                if (el is null)
                {
                    if (HasAnyValue(sheet, row))
                    {
                        var missing = new ScheduleCellChange
                        {
                            ScheduleName = name, SheetName = sheet.SheetName, ElementId = row.ElementId,
                            ElementLabel = "Id " + row.ElementId, ColumnHeading = "—",
                            Status = ChangeStatus.Error, Message = "El elemento no existe en el modelo.",
                        };
                        missing.ExcelCells.Add((row.ExcelRow, sheet.IdExcelColumn));
                        preview.Changes.Add(missing);
                    }
                    continue;
                }

                for (int i = 0; i < sheet.Columns.Count; i++)
                {
                    var col = sheet.Columns[i];
                    if (!col.Editable || i >= row.Values.Count) continue;
                    var raw = row.Values[i];
                    if (raw is Missing) continue;

                    var change = Compare(doc, units, el, col, raw);
                    if (change is null) continue;
                    change.ScheduleName = name;
                    change.SheetName = sheet.SheetName;
                    if (i < sheet.ColumnExcelIndex.Count && sheet.ColumnExcelIndex[i] > 0)
                        change.ExcelCells.Add((row.ExcelRow, sheet.ColumnExcelIndex[i]));
                    preview.Changes.Add(change);
                }
            }
        }

        foreach (var sheet in file.PresentationSheetsData)
            PreviewPresentation(doc, units, sheet, preview, Step);
        progress?.Invoke(1);

        ResolveTypeChanges(doc, preview.Changes);
        FlagCrossRowTypeConflicts(preview.Changes);
        foreach (var c in preview.Changes) c.IsSelected = c.Status == ChangeStatus.Ready;
        return preview;
    }

    // ====================================================================
    // Pestañas «como en Revit»: cada fila puede agrupar muchos elementos
    // ====================================================================

    private static void PreviewPresentation(Document doc, Units units, ParsedPresentationSheet sheet, ScheduleImportPreview preview, Action step)
    {
        var name = sheet.ScheduleName ?? sheet.SheetName;
        preview.ScheduleNames.Add(name);
        preview.Warnings.AddRange(sheet.Warnings);
        preview.ExportedAt ??= sheet.ExportedAt;
        preview.RowsRead += sheet.Rows.Count;
        if (!string.IsNullOrEmpty(sheet.DocumentTitle) &&
            !string.Equals(sheet.DocumentTitle, doc.Title, StringComparison.OrdinalIgnoreCase))
            preview.DocumentMatches = false;

        foreach (var row in sheet.Rows)
        {
            step();
            bool moved = RowMoved(sheet, row);
            for (int c = 0; c < sheet.Columns.Count && c < row.Values.Count; c++)
            {
                var col = sheet.Columns[c];
                if (!col.Editable) continue;
                var original = c < row.Originals.Count ? row.Originals[c] : "";
                var raw = row.Values[c];
                if (!ChangedFromOriginal(raw, original, sheet)) continue;

                var change = new ScheduleCellChange
                {
                    ScheduleName = name,
                    ElementLabel = row.Label,
                    ColumnHeading = col.Heading,
                    ParameterId = col.ParameterId,
                    IsType = col.IsType,
                    Kind = col.Kind,
                    OldText = original,
                    NewText = RawText(raw),
                    SheetName = sheet.SheetName,
                };
                change.ExcelCells.Add((row.ExcelRow, c + 1));

                if (moved) { preview.Changes.Add(Fail(change, "Las columnas de solo lectura de esta fila no coinciden con lo exportado (¿se han ordenado o borrado filas en Excel?). Vuelve a exportar la tabla.")); continue; }
                if (row.Ambiguous) { preview.Changes.Add(Fail(change, "Hay otras filas con los mismos datos visibles: no se sabe a qué elementos aplicarlo. Usa el formato «Editable».")); continue; }
                if (row.ElementIds.Count == 0) { preview.Changes.Add(Fail(change, "No se identificaron los elementos de esta fila al exportar.")); continue; }
                if (raw is null)
                {
                    change.NewText = "(vacío)";
                    if (col.Kind != EditableKind.Text)
                    {
                        preview.Changes.Add(Fail(change, "Revit no permite dejar vacío un parámetro numérico o Sí/No: escribe un valor."));
                        continue;
                    }
                }

                // Un cambio por elemento → se agrupan en uno solo para la fila
                var ready = new List<ScheduleCellChange>();
                var errors = new List<ScheduleCellChange>();
                int missing = 0;
                foreach (var id in row.ElementIds)
                {
                    var el = doc.GetElement(new ElementId(id));
                    if (el is null) { missing++; continue; }
                    var one = Compare(doc, units, el, col, raw);
                    if (one is null) continue;
                    (one.Status == ChangeStatus.Ready ? ready : errors).Add(one);
                }
                if (ready.Count == 0 && errors.Count == 0) continue; // ya tienen ese valor

                if (ready.Count == 0)
                {
                    preview.Changes.Add(Fail(change, errors[0].Message ?? "No se puede aplicar."));
                    continue;
                }

                change.Status = ChangeStatus.Ready;
                change.NewValue = ready[0].NewValue;
                change.NewText = ready[0].NewText;
                change.ElementIds.AddRange(ready.Select(x => x.ElementId).Distinct());
                change.ElementId = change.ElementIds[0];
                int n = change.ElementIds.Count;
                change.ElementLabel = row.Label + " (" + n + (col.IsType ? (n == 1 ? " tipo)" : " tipos)") : (n == 1 ? " elemento)" : " elementos)"));
                var olds = ready.Select(x => x.OldText).Distinct().ToList();
                change.OldText = olds.Count == 1 ? (olds[0].Length == 0 ? "(vacío)" : olds[0]) : "varios (" + olds.Count + " valores)";
                var notes = new List<string>();
                if (errors.Count > 0) notes.Add(errors.Count + " elementos no se cambiarán: " + errors[0].Message);
                if (missing > 0) notes.Add(missing + " elementos ya no existen.");
                change.Message = notes.Count > 0 ? string.Join(" ", notes) : null;
                preview.Changes.Add(change);
            }
        }
    }

    /// <summary>
    /// Si el usuario ha ordenado o filtrado el Excel, las filas ya no estan donde
    /// se exportaron: se comprueban las celdas que no son editables.
    /// </summary>
    private static bool RowMoved(ParsedPresentationSheet sheet, PresentationRow row)
    {
        for (int c = 0; c < sheet.Columns.Count && c < row.Values.Count && c < row.Originals.Count; c++)
        {
            // Solo columnas de texto no editables (p. ej. «Familia y tipo»): un total
            // numerico tocado sin querer no indica que la fila se haya movido.
            if (sheet.Columns[c].Editable || sheet.Columns[c].Kind == EditableKind.Number) continue;
            var original = row.Originals[c];
            if (original.Length == 0) continue;
            if (ChangedFromOriginal(row.Values[c], original, sheet)) return true;
        }
        return false;
    }

    /// <summary>
    /// Celda distinta del texto exportado. Una celda vacía es un cambio solo si
    /// al exportar tenía valor (el usuario la ha borrado); si ya estaba vacía
    /// —vacía o «varía» en tablas agrupadas— no lo es.
    /// </summary>
    internal static bool ChangedFromOriginal(object? raw, string original, ParsedPresentationSheet sheet)
    {
        switch (raw)
        {
            case Missing:
                return false;
            case null:
                return PresentationNorm(original).Length > 0;
            case double d:
                if (ScheduleExcelWriter.TryParseNumber(original, sheet.DecimalSeparator, sheet.GroupSeparator, out var o, out _, out _))
                    return Math.Abs(d - o) > 1e-9 * Math.Max(1, Math.Abs(o));
                return true;
            case bool b:
                return ParseYesNo(original) != b;
            default:
                return !string.Equals(PresentationNorm(Convert.ToString(raw, CultureInfo.CurrentCulture)),
                                      PresentationNorm(original), StringComparison.Ordinal);
        }
    }

    private static string PresentationNorm(string? s) =>
        string.Join(" ", (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Dos cambios (de filas distintas) que dan valores diferentes al mismo parametro de un tipo.</summary>
    private static void FlagCrossRowTypeConflicts(List<ScheduleCellChange> changes)
    {
        var byTarget = new Dictionary<(long, long), List<ScheduleCellChange>>();
        foreach (var c in changes.Where(c => c.IsType && c.Status == ChangeStatus.Ready))
            foreach (var id in c.ElementIds.Count > 0 ? c.ElementIds : new List<long> { c.ElementId })
            {
                if (!byTarget.TryGetValue((id, c.ParameterId), out var list)) byTarget[(id, c.ParameterId)] = list = new();
                list.Add(c);
            }
        foreach (var list in byTarget.Values)
        {
            if (list.Select(c => c.NewText).Distinct(StringComparer.Ordinal).Count() < 2) continue;
            foreach (var c in list)
            {
                c.Status = ChangeStatus.Conflict;
                c.Message = "Otras filas dan otro valor a este parámetro del mismo tipo.";
            }
        }
    }

    private static bool HasAnyValue(ParsedEditableWorkbook sheet, EditableRow row) =>
        row.Values.Where((v, i) => i < sheet.Columns.Count && sheet.Columns[i].Editable)
                  .Any(v => v is not null && v is not Missing);

    /// <summary>Devuelve el cambio, o null si el valor del Excel coincide con el del modelo.</summary>
    private static ScheduleCellChange? Compare(Document doc, Units units, Element el, EditableColumn col, object? raw)
    {
        var owner = ScheduleEditableReader.Owner(doc, el, col.IsType);
        var p = ScheduleEditableReader.FindParameter(owner, new ElementId(col.ParameterId));

        var change = new ScheduleCellChange
        {
            ElementId = col.IsType && owner is not null ? owner.Id.Value : el.Id.Value,
            ElementLabel = Label(doc, el, col.IsType ? owner : null),
            ColumnHeading = col.Heading,
            ParameterId = col.ParameterId,
            IsType = col.IsType,
            Kind = col.Kind,
            NewText = RawText(raw),
        };

        if (p is null)
        {
            if (raw is null || (raw is string s0 && s0.Length == 0)) return null;
            return Fail(change, "El elemento no tiene este parámetro.");
        }
        change.OldText = (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString()) ?? "";
        if (p.IsReadOnly) return Fail(change, "Parámetro de solo lectura.");

        switch (col.Kind)
        {
            case EditableKind.Text:
            {
                var newText = raw switch
                {
                    null => "",
                    double d => d.ToString("0.##########", CultureInfo.CurrentCulture),
                    bool b => b ? "Sí" : "No",
                    _ => Convert.ToString(raw, CultureInfo.CurrentCulture) ?? "",
                };
                if (string.Equals(newText, p.AsString() ?? "", StringComparison.Ordinal)) return null;
                change.NewText = newText.Length == 0 ? "(vacío)" : newText;
                change.NewValue = newText;
                break;
            }
            case EditableKind.Integer:
            {
                if (raw is null) return null;
                int v;
                if (raw is double d && Math.Abs(d - Math.Round(d)) < 1e-9) v = (int)Math.Round(d);
                else if (!int.TryParse(RawText(raw).Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v))
                    return Fail(change, "Se esperaba un número entero.");
                if (v == p.AsInteger()) return null;
                change.NewText = v.ToString(CultureInfo.CurrentCulture);
                change.NewValue = v;
                break;
            }
            case EditableKind.YesNo:
            {
                if (raw is null) return null;
                bool? v = raw switch
                {
                    bool b => b,
                    double d => d != 0,
                    _ => ParseYesNo(RawText(raw)),
                };
                if (v is null) return Fail(change, "Se esperaba Sí o No.");
                if ((p.AsInteger() != 0) == v.Value) return null;
                change.NewText = v.Value ? "Sí" : "No";
                change.NewValue = v.Value ? 1 : 0;
                break;
            }
            case EditableKind.Number:
            {
                if (raw is null) return null;
                double internalValue;
                if (raw is double d)
                {
                    internalValue = d;
                    if (col.UnitTypeId is not null)
                    {
                        try { internalValue = UnitUtils.ConvertToInternalUnits(d, new ForgeTypeId(col.UnitTypeId)); }
                        catch { return Fail(change, "No se pudo convertir la unidad."); }
                    }
                }
                else if (!TryParseNumber(units, col, RawText(raw), out internalValue))
                {
                    return Fail(change, "No es un número válido.");
                }

                double old = p.AsDouble();
                double tol = Math.Max(1e-9, Math.Abs(old) * 1e-7);
                if (Math.Abs(internalValue - old) <= tol) return null;
                change.NewValue = internalValue;
                change.NewText = FormatNumber(units, col, internalValue) ?? change.NewText;
                break;
            }
            default:
                return null;
        }

        if (doc.IsWorkshared)
        {
            try
            {
                var target = col.IsType && owner is not null ? owner.Id : el.Id;
                var status = WorksharingUtils.GetCheckoutStatus(doc, target, out string ownerName);
                if (status == CheckoutStatus.OwnedByOtherUser)
                    return Fail(change, "Elemento prestado a " + ownerName + ".");
            }
            catch { /* sin informacion de prestamo */ }
        }

        change.Status = ChangeStatus.Ready;
        return change;
    }

    /// <summary>
    /// Los parametros de tipo aparecen una vez por elemento: se agrupan en un
    /// solo cambio por tipo. Si las filas piden valores distintos, es un conflicto.
    /// </summary>
    private static void ResolveTypeChanges(Document doc, List<ScheduleCellChange> changes)
    {
        var groups = changes
            .Where(c => c.IsType && c.ElementIds.Count == 0 && (c.Status == ChangeStatus.Ready || c.Status == ChangeStatus.Error))
            .GroupBy(c => (c.ElementId, c.ParameterId))
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var g in groups)
        {
            var items = g.ToList();
            var first = items[0];
            bool conflict = items.Select(c => c.NewText).Distinct(StringComparer.Ordinal).Count() > 1;
            foreach (var extra in items.Skip(1))
            {
                first.ExcelCells.AddRange(extra.ExcelCells);
                changes.Remove(extra);
            }

            var type = doc.GetElement(new ElementId(first.ElementId));
            first.ElementLabel = "Tipo " + (type is ElementType et ? et.FamilyName + ": " + et.Name : type?.Name)
                                 + " (" + items.Count + " filas)";
            if (conflict && first.Status == ChangeStatus.Ready)
            {
                first.Status = ChangeStatus.Conflict;
                first.Message = "Filas del mismo tipo con valores distintos: " +
                                string.Join(" / ", items.Select(c => c.NewText).Distinct().Take(4)) + ".";
            }
        }
    }

    // ====================================================================
    // Aplicar
    // ====================================================================

    public static ScheduleImportResult Apply(Document doc, IReadOnlyList<ScheduleCellChange> changes, Action<double>? progress = null)
    {
        var todo = changes.Where(c => c.IsSelected && c.Status == ChangeStatus.Ready).ToList();
        var result = new ScheduleImportResult();
        if (todo.Count == 0) { result.Success = true; return result; }

        using var tx = new Transaction(doc, "TableLink — Importar datos");
        var options = tx.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new WarningSwallower());
        tx.SetFailureHandlingOptions(options);
        tx.Start();

        int totalTargets = Math.Max(1, todo.Sum(c => c.TargetCount));
        int doneTargets = 0;
        foreach (var c in todo)
        {
            var targets = c.ElementIds.Count > 0 ? c.ElementIds : new List<long> { c.ElementId };
            int ok = 0;
            string? firstError = null;
            foreach (var id in targets)
            {
                if (++doneTargets % 25 == 0) progress?.Invoke(0.95 * doneTargets / totalTargets);
                try
                {
                    var el = doc.GetElement(new ElementId(id));
                    var p = ScheduleEditableReader.FindParameter(el, new ElementId(c.ParameterId));
                    if (p is null) { firstError ??= "Parámetro no encontrado."; continue; }
                    bool set = c.NewValue switch
                    {
                        string s => p.Set(s),
                        int i => p.Set(i),
                        double d => p.Set(d),
                        _ => false,
                    };
                    if (set) ok++;
                    else firstError ??= "Revit no aceptó el valor.";
                }
                catch (Exception ex)
                {
                    firstError ??= ex.Message;
                }
            }

            if (ok == targets.Count) Mark(c, ChangeStatus.Applied, c.Message);
            else if (ok > 0) Mark(c, ChangeStatus.Applied, (targets.Count - ok) + " de " + targets.Count + " no se cambiaron: " + firstError);
            else Mark(c, ChangeStatus.Failed, firstError);
        }

        var status = tx.Commit();
        if (status != TransactionStatus.Committed)
        {
            foreach (var c in todo.Where(x => x.Status == ChangeStatus.Applied))
                Mark(c, ChangeStatus.Failed, "La transacción no se pudo confirmar.");
            result.ErrorMessage = "Revit no pudo confirmar los cambios (" + status + ").";
        }

        result.Applied = todo.Count(c => c.Status == ChangeStatus.Applied);
        result.Failed = todo.Count(c => c.Status == ChangeStatus.Failed);
        result.Success = result.ErrorMessage is null;
        return result;
    }

    private static void Mark(ScheduleCellChange c, ChangeStatus s, string? msg)
    {
        c.Status = s;
        c.Message = msg;
        if (s != ChangeStatus.Ready) c.IsSelected = false;
    }

    private sealed class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            foreach (var f in fa.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
            return FailureProcessingResult.Continue;
        }
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static ScheduleCellChange Fail(ScheduleCellChange c, string msg)
    {
        c.Status = ChangeStatus.Error;
        c.Message = msg;
        return c;
    }

    private static string RawText(object? raw) => raw switch
    {
        null => "",
        double d => d.ToString("0.##########", CultureInfo.CurrentCulture),
        bool b => b ? "Sí" : "No",
        _ => Convert.ToString(raw, CultureInfo.CurrentCulture) ?? "",
    };

    internal static bool? ParseYesNo(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (TrueWords.Contains(t)) return true;
        if (FalseWords.Contains(t)) return false;
        return null;
    }

    private static bool TryParseNumber(Units units, EditableColumn col, string text, out double internalValue)
    {
        internalValue = 0;
        if (col.SpecTypeId is not null)
        {
            try
            {
                if (UnitFormatUtils.TryParse(units, new ForgeTypeId(col.SpecTypeId), text, out internalValue))
                    return true;
            }
            catch { /* se intenta como numero simple */ }
        }
        var t = text.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ||
            double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
        {
            internalValue = v;
            if (col.UnitTypeId is not null)
            {
                try { internalValue = UnitUtils.ConvertToInternalUnits(v, new ForgeTypeId(col.UnitTypeId)); }
                catch { return false; }
            }
            return true;
        }
        return false;
    }

    private static string? FormatNumber(Units units, EditableColumn col, double internalValue)
    {
        if (col.SpecTypeId is null) return null;
        try { return UnitFormatUtils.Format(units, new ForgeTypeId(col.SpecTypeId), internalValue, false); }
        catch { return null; }
    }

    private static string Label(Document doc, Element el, Element? type)
    {
        if (type is ElementType et) return "Tipo " + et.FamilyName + ": " + et.Name;
        var t = doc.GetElement(el.GetTypeId()) as ElementType;
        var name = t is null ? el.Name : t.FamilyName + ": " + t.Name;
        return name + " [" + el.Id.Value + "]";
    }
}
