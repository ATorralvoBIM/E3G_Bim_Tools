using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;
using E3G.BimTools.DiTools.Core.Services.ScheduleExcel;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.ScheduleExcel;

public class ScheduleExcelWriterTests
{
    [Theory]
    [InlineData("382,26", 382.26, 2, null)]
    [InlineData("40", 40, 0, null)]
    [InlineData("1.234,5 m", 1234.5, 1, "m")]
    [InlineData("12 %", 12, 0, "%")]
    [InlineData("-3,50", -3.5, 2, null)]
    [InlineData("0,75 m²", 0.75, 2, "m²")]
    public void TryParseNumber_SpanishUnits_Parses(string text, double expected, int decimals, string? unit)
    {
        ScheduleExcelWriter.TryParseNumber(text, ',', '.', out var v, out var d, out var u).Should().BeTrue();
        v.Should().BeApproximately(expected, 1e-9);
        d.Should().Be(decimals);
        u.Should().Be(unit);
    }

    [Theory]
    [InlineData("P00")]
    [InlineData("007")]
    [InlineData("1 1/2''")]
    [InlineData("Tipos de tubería: PIPE_PVC-U")]
    [InlineData("1.5")]          // con miles '.', no es un numero valido
    [InlineData("12:30")]
    [InlineData("10 unidades por planta")]
    [InlineData("")]
    public void TryParseNumber_NonNumbers_StayText(string text)
    {
        ScheduleExcelWriter.TryParseNumber(text, ',', '.', out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Write_GroupedSchedule_ProducesCorporateLayout()
    {
        var snap = new ScheduleSnapshot { Name = "E3G_MEDICIONES_TUBERIAS_SANEAMIENTO", ScheduleUniqueId = "abc-123" };
        snap.TitleLines.Add(snap.Name);
        snap.ColumnWidthsMm.AddRange(new[] { 55.3, 25.4, 30.7, 25.4 });
        snap.HeadingRows.Add(new[] { "Familia y tipo", "Diámetro", "Longitud", "E3G_Codi_PO" }
            .Select(t => new ScheduleCell { Text = t }).ToList());
        snap.Rows.Add(Group("10_10_Fecals", 1));
        snap.Rows.Add(Group("Tipos de tubería: PIPE_PP_TRICAPA", 2));
        snap.Rows.Add(Data("Tipos de tubería: PIPE_PP_TRICAPA", "40", "382,26", ""));
        snap.Rows.Add(Data("Tipos de tubería: PIPE_PP_TRICAPA", "110", "1111,39", ""));

        var path = Path.Combine(Path.GetTempPath(), "e3g_schedexcel_" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ScheduleExcelWriter.Write(snap, path).Should().Be(4);

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(1);
            ws.Name.Should().Be("E3G_MEDICIONES_TUBERIAS_SANEAMI");          // 31 caracteres
            ws.Cell(1, 1).GetString().Should().Be(snap.Name);
            ws.Range("A1:D1").IsMerged().Should().BeTrue();
            ws.Cell(1, 1).Style.Fill.BackgroundColor.Color.ToArgb().Should().Be(unchecked((int)0xFF1F4E78));
            ws.Cell(2, 2).GetString().Should().Be("Diámetro");
            ws.Cell(2, 1).Style.Font.Bold.Should().BeTrue();

            ws.Cell(3, 1).GetString().Should().Be("10_10_Fecals");
            ws.Range("A3:D3").IsMerged().Should().BeTrue();
            ws.Cell(4, 1).Style.Font.Italic.Should().BeTrue();

            ws.Cell(5, 2).GetValue<double>().Should().Be(40);
            ws.Cell(5, 3).GetValue<double>().Should().BeApproximately(382.26, 1e-9);
            ws.Cell(5, 3).Style.Fill.BackgroundColor.Color.ToArgb().Should().Be(unchecked((int)0xFFD1E9E9));
            ws.Cell(6, 3).Style.NumberFormat.Format.Should().Be("#,##0.00");
            ws.SheetView.SplitRow.Should().Be(2);

            var meta = wb.Worksheet(ScheduleExcelWriter.MetaSheetName);
            meta.Visibility.Should().Be(XLWorksheetVisibility.Hidden);
            var m = ScheduleWorkbookMeta.Read(wb)[ws.Name];
            m["ScheduleUniqueId"].Should().Be("abc-123");
            m["Mode"].Should().Be(ScheduleExcelWriter.ModePresentation);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AssignGroupLevels_RunsMapToInnermostLevels()
    {
        var rows = new[]
        {
            Group("Fecals", 0), Group("PP", 0), Data("a"), Group("PVC", 0), Data("b"),
            Group("Pluvials", 0), Group("PP", 0), Data("c"),
        }.ToList();

        ScheduleSnapshotReader.AssignGroupLevels(rows, 2);

        rows.Where(r => r.Kind == ScheduleRowKind.Group).Select(r => r.Level)
            .Should().Equal(1, 2, 2, 1, 2);
    }

    private static ScheduleRow Group(string text, int level)
    {
        var r = new ScheduleRow { Kind = ScheduleRowKind.Group, Level = level };
        r.Cells.Add(new ScheduleCell { Text = text, ColSpan = 4 });
        return r;
    }

    private static ScheduleRow Data(params string[] texts)
    {
        var r = new ScheduleRow { Kind = ScheduleRowKind.Data };
        for (int i = 0; i < texts.Length; i++)
            r.Cells.Add(new ScheduleCell { Text = texts[i], BackgroundHex = i == 2 ? "D1E9E9" : null });
        return r;
    }
}
