using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.Core.Services.ScheduleExcel;

/// <summary>
/// Lee un <see cref="ViewSchedule"/> tal y como se ve en pantalla (textos ya
/// formateados con las unidades del proyecto) y lo convierte en un
/// <see cref="ScheduleSnapshot"/>.
///
/// Estructura del cuerpo de una tabla de Revit:
///   filas de encabezado de columna → fila en blanco → datos.
/// Los encabezados de grupo son filas con la 1ª celda combinada en todo el
/// ancho; su nivel se deduce de cuantos aparecen seguidos (al cambiar un grupo
/// de nivel k, Revit repite los encabezados de k..N).
/// </summary>
internal static class ScheduleSnapshotReader
{
    private const double FtToMm = 304.8;
    private const int MaxHeadingRows = 10;

    public static ScheduleSnapshot Read(ViewSchedule vs) => Read(vs, mapRows: true, progress: null);

    /// <param name="mapRows">Averiguar los elementos de cada fila (necesario para reimportar; tarda unos segundos en tablas grandes).</param>
    public static ScheduleSnapshot Read(ViewSchedule vs, bool mapRows, Action<double>? progress)
    {
        if (vs is null) throw new ArgumentNullException(nameof(vs));
        var doc = vs.Document;
        var td = vs.GetTableData();
        var body = td.GetSectionData(SectionType.Body)
                   ?? throw new InvalidOperationException("La tabla no tiene cuerpo.");

        var snap = new ScheduleSnapshot
        {
            Name = vs.Name,
            ScheduleUniqueId = vs.UniqueId,
            DocumentTitle = doc?.Title,
        };
        ReadSeparators(doc, snap);

        int c0 = body.FirstColumnNumber, c1 = body.LastColumnNumber;
        for (int c = c0; c <= c1; c++)
            snap.ColumnWidthsMm.Add(body.GetColumnWidth(c) * FtToMm);

        // ── Titulo (seccion Header) ────────────────────────────────────
        var header = td.GetSectionData(SectionType.Header);
        if (header is not null && header.NumberOfRows > 0)
        {
            for (int r = header.FirstRowNumber; r <= header.LastRowNumber; r++)
            {
                var parts = new List<string>();
                for (int c = header.FirstColumnNumber; c <= header.LastColumnNumber; c++)
                {
                    var t = Text(vs, SectionType.Header, r, c);
                    if (t.Length > 0 && !parts.Contains(t)) parts.Add(t);
                }
                if (parts.Count > 0) snap.TitleLines.Add(string.Join("  ", parts));
            }
        }

        // ── Encabezados de columna ─────────────────────────────────────
        int r0 = body.FirstRowNumber, rN = body.LastRowNumber;
        int headingCount = 0;
        if (vs.Definition.ShowHeaders && rN >= r0)
        {
            headingCount = 1;
            for (int r = r0; r <= Math.Min(rN, r0 + MaxHeadingRows); r++)
            {
                if (IsBlankRow(vs, r, c0, c1)) { headingCount = r - r0; break; }
            }
        }

        for (int r = r0; r < r0 + headingCount; r++)
            snap.HeadingRows.Add(ReadCells(vs, body, r, c0, c1, withStyle: false));

        // ── Cuerpo ─────────────────────────────────────────────────────
        for (int r = r0 + headingCount; r <= rN; r++)
        {
            if (IsBlankRow(vs, r, c0, c1)) continue;

            var row = new ScheduleRow();
            if (c1 > c0 && IsFullWidthMerge(body, r, c0, c1))
            {
                row.Kind = ScheduleRowKind.Group;
                row.Cells.Add(new ScheduleCell { Text = Text(vs, SectionType.Body, r, c0), ColSpan = c1 - c0 + 1 });
            }
            else
            {
                row.Kind = ScheduleRowKind.Data;
                row.Cells.AddRange(ReadCells(vs, body, r, c0, c1, withStyle: true));
            }
            snap.Rows.Add(row);
        }

        AssignGroupLevels(snap.Rows, CountGroupHeaders(vs.Definition));

        // Elementos de cada fila, para poder reimportar cambios hechos en esta misma tabla.
        // Nunca impide exportar: si falla, la pestaña sale solo para consulta.
        if (!mapRows) return snap;
        try { PresentationRowMapper.Map(vs, snap, progress); }
        catch
        {
            snap.ColumnFields.Clear();
            foreach (var r in snap.Rows) r.ElementIds = null;
        }
        return snap;
    }

    // ====================================================================

    /// <summary>Asigna el nivel a cada encabezado de grupo segun las rachas consecutivas.</summary>
    internal static void AssignGroupLevels(IList<ScheduleRow> rows, int groupHeaderFields)
    {
        int i = 0;
        while (i < rows.Count)
        {
            if (rows[i].Kind != ScheduleRowKind.Group) { i++; continue; }
            int j = i;
            while (j < rows.Count && rows[j].Kind == ScheduleRowKind.Group) j++;
            int run = j - i;
            int start = groupHeaderFields >= run ? groupHeaderFields - run + 1 : 1;
            for (int k = 0; k < run; k++) rows[i + k].Level = start + k;
            i = j;
        }
    }

    private static int CountGroupHeaders(ScheduleDefinition def)
    {
        int n = 0;
        try
        {
            int count = def.GetSortGroupFieldCount();
            for (int i = 0; i < count; i++)
                if (def.GetSortGroupField(i).ShowHeader) n++;
        }
        catch { /* sin agrupaciones legibles: niveles por racha */ }
        return n;
    }

    private static List<ScheduleCell> ReadCells(ViewSchedule vs, TableSectionData sec, int r, int c0, int c1, bool withStyle)
    {
        var cells = new List<ScheduleCell>(c1 - c0 + 1);
        for (int c = c0; c <= c1; c++)
        {
            var cell = new ScheduleCell();
            var m = Merged(sec, r, c);
            if (m is not null)
            {
                if (m.Top != r || m.Left != c) { cell.Covered = true; cells.Add(cell); continue; }
                cell.ColSpan = Math.Max(1, Math.Min(m.Right, c1) - m.Left + 1);
                cell.RowSpan = Math.Max(1, m.Bottom - m.Top + 1);
            }

            cell.Text = Text(vs, SectionType.Body, r, c);
            if (withStyle)
            {
                try
                {
                    var st = sec.GetTableCellStyle(r, c);
                    var bg = st.BackgroundColor;
                    if (bg is not null && bg.IsValid && !(bg.Red == 255 && bg.Green == 255 && bg.Blue == 255))
                        cell.BackgroundHex = bg.Red.ToString("X2") + bg.Green.ToString("X2") + bg.Blue.ToString("X2");
                    cell.Bold = st.IsFontBold;
                }
                catch { /* estilo no disponible en esta celda */ }
            }
            cells.Add(cell);
        }
        return cells;
    }

    private static TableMergedCell? Merged(TableSectionData sec, int r, int c)
    {
        try
        {
            var m = sec.GetMergedCell(r, c);
            if (m is null || (m.Top == m.Bottom && m.Left == m.Right)) return null;
            return m;
        }
        catch { return null; }
    }

    private static bool IsFullWidthMerge(TableSectionData sec, int r, int c0, int c1)
    {
        var m = Merged(sec, r, c0);
        return m is not null && m.Left <= c0 && m.Right >= c1 && m.Top == r && m.Bottom == r;
    }

    private static bool IsBlankRow(ViewSchedule vs, int r, int c0, int c1)
    {
        for (int c = c0; c <= c1; c++)
            if (Text(vs, SectionType.Body, r, c).Length > 0) return false;
        return true;
    }

    private static string Text(ViewSchedule vs, SectionType st, int r, int c)
    {
        try { return (vs.GetCellText(st, r, c) ?? "").Trim(); }
        catch { return ""; }
    }

    private static void ReadSeparators(Document? doc, ScheduleSnapshot snap)
    {
        if (doc is null) return;
        try
        {
            var units = doc.GetUnits();
            snap.DecimalSeparator = units.DecimalSymbol == DecimalSymbol.Dot ? '.' : ',';

            var g = units.DigitGroupingSymbol;
            char? grp =
                g == DigitGroupingSymbol.Dot ? '.' :
                g == DigitGroupingSymbol.Comma ? ',' :
                g == DigitGroupingSymbol.Space ? ' ' :
                g == DigitGroupingSymbol.Apostrophe || g == DigitGroupingSymbol.Tick ? '\'' :
                null;
            snap.GroupSeparator = grp == snap.DecimalSeparator ? null : grp;
        }
        catch { /* se quedan los separadores por defecto (es-ES) */ }
    }
}
