using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;
using E3G.BimTools.DiTools.Core.Services.ScheduleExcel;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.ScheduleExcel;

public class SheetGridBuilderTests
{
    private static ScheduleSnapshot Sample()
    {
        var snap = new ScheduleSnapshot { Name = "Tuberías" };
        snap.ColumnWidthsMm.AddRange(new[] { 55.3, 25.4, 30.7 });
        snap.HeadingRows.Add(new[] { "Familia y tipo", "Diámetro", "Longitud" }.Select(t => new ScheduleCell { Text = t }).ToList());
        var g = new ScheduleRow { Kind = ScheduleRowKind.Group, Level = 1 };
        g.Cells.Add(new ScheduleCell { Text = "10_10_Fecals", ColSpan = 3 });
        snap.Rows.Add(g);
        var d = new ScheduleRow { Kind = ScheduleRowKind.Data };
        d.Cells.Add(new ScheduleCell { Text = "PIPE_PP" });
        d.Cells.Add(new ScheduleCell { Text = "40" });
        d.Cells.Add(new ScheduleCell { Text = "382,26", BackgroundHex = "D1E9E9" });
        snap.Rows.Add(d);
        return snap;
    }

    [Fact]
    public void FromSnapshot_KeepsExcelLayout()
    {
        var grid = SheetGridBuilder.FromSnapshot(Sample(), 100);
        grid.Rows.Should().HaveCount(4);                       // título, encabezado, grupo, datos
        grid.Rows[0].Cells[0].ColSpan.Should().Be(3);
        grid.Rows[0].Cells[0].Background.Should().Be("1F4E78");
        grid.Rows[2].Cells[0].Background.Should().Be("BDD7EE");
        grid.Rows[3].Cells[2].Background.Should().Be("D1E9E9");
        grid.Rows[3].Cells[2].Align.Should().Be(GridAlign.Right);
        grid.ColumnWidths.Should().HaveCount(3);
        grid.Truncated.Should().BeFalse();
    }

    [Fact]
    public void FromWorkbook_ReadsWhatWasWritten_WithExcelRowNumbers()
    {
        var path = Path.Combine(Path.GetTempPath(), "e3g_grid_" + Guid.NewGuid().ToString("N") + ".xlsx");
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            ScheduleExcelWriter.Write(Sample(), path);
            var grids = SheetGridBuilder.FromWorkbook(path, 100);
            grids.Should().HaveCount(1);                       // las hojas ocultas no salen
            var g = grids[0];
            g.ScheduleName.Should().Be("Tuberías");
            var data = g.Rows.Single(r => r.ExcelRow == 4);
            data.Cells.Single(c => c.Column == 3).Text.Should().Be("382,26");
            data.Cells.Single(c => c.Column == 3).Background.Should().Be("D1E9E9");
            g.Rows[0].Cells[0].ColSpan.Should().Be(3);         // título combinado
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FromSnapshot_TruncatesLongTables()
    {
        var snap = Sample();
        for (int i = 0; i < 50; i++)
        {
            var d = new ScheduleRow { Kind = ScheduleRowKind.Data };
            d.Cells.Add(new ScheduleCell { Text = "x" + i });
            snap.Rows.Add(d);
        }
        var grid = SheetGridBuilder.FromSnapshot(snap, 10);
        grid.Rows.Should().HaveCount(10);
        grid.Truncated.Should().BeTrue();
    }
}
