using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;
using E3G.BimTools.DiTools.Core.Services.ScheduleExcel;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.ScheduleExcel;

public class ScheduleEditableWorkbookTests
{
    private static ScheduleEditableData Sample(string name, string uid)
    {
        var d = new ScheduleEditableData { Name = name, ScheduleUniqueId = uid, DocumentTitle = "25-144_MEP" };
        d.Columns.Add(new EditableColumn { Heading = "Familia y tipo", Key = "-1002052:I", ParameterId = -1002052, Kind = EditableKind.Text, Editable = false });
        d.Columns.Add(new EditableColumn { Heading = "E3G_Codi_PO", Key = "123:I", ParameterId = 123, Kind = EditableKind.Text, Editable = true });
        d.Columns.Add(new EditableColumn { Heading = "Longitud", Key = "-1004005:I", ParameterId = -1004005, Kind = EditableKind.Number, Editable = false, UnitTypeId = "autodesk.unit.unit:meters-1.0.1", Decimals = 2 });
        d.Columns.Add(new EditableColumn { Heading = "Aislado", Key = "456:T", ParameterId = 456, IsType = true, Kind = EditableKind.YesNo, Editable = true });
        foreach (var (id, len) in new[] { (1001L, 382.26), (1002L, 220.37) })
        {
            var r = new EditableRow { ElementId = id };
            r.Values.AddRange(new object?[] { "Tuberías: PIPE_PP_TRICAPA", null, len, true });
            d.Rows.Add(r);
        }
        return d;
    }

    private static string TempXlsx() =>
        Path.Combine(Path.GetTempPath(), "e3g_sched_" + Guid.NewGuid().ToString("N") + ".xlsx");

    [Fact]
    public void Editable_RoundTrip_ReadsUserEdits()
    {
        var path = TempXlsx();
        try
        {
            ScheduleEditableWorkbook.Write(Sample("Tuberías", "uid-1"), path);

            // El usuario rellena el código en Excel, cambia el Sí/No y reordena columnas
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet(1);
                ws.Cell(3, 1).GetString().Should().Be(ScheduleEditableWorkbook.IdHeading);
                ws.Cell(3, 3).GetString().Should().Be("E3G_Codi_PO");
                ws.Cell(4, 3).Value = "PO-01";
                ws.Cell(5, 3).Value = 77;          // número escrito en una columna de texto
                ws.Cell(4, 5).Value = "No";
                ws.Column(2).Delete();             // borra «Familia y tipo» (solo lectura)
                wb.Save();
            }

            var file = ScheduleEditableWorkbook.Read(path);
            file.Sheets.Should().HaveCount(1);
            var sheet = file.Sheets[0];
            sheet.ScheduleUniqueId.Should().Be("uid-1");
            sheet.Rows.Select(r => r.ElementId).Should().Equal(1001, 1002);

            int code = sheet.Columns.FindIndex(c => c.Heading == "E3G_Codi_PO");
            int yesNo = sheet.Columns.FindIndex(c => c.Heading == "Aislado");
            int family = sheet.Columns.FindIndex(c => c.Heading == "Familia y tipo");
            sheet.Rows[0].Values[code].Should().Be("PO-01");
            sheet.Rows[1].Values[code].Should().Be(77d);
            sheet.Rows[0].Values[yesNo].Should().Be("No");
            sheet.Rows[0].Values[family].Should().Be(Missing.Value);   // columna borrada → no tocar
            sheet.Columns[yesNo].IsType.Should().BeTrue();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void MultipleSchedules_GoToSeparateTabs_AndAllAreImportable()
    {
        var path = TempXlsx();
        try
        {
            using (var wb = new XLWorkbook())
            {
                ScheduleEditableWorkbook.WriteSheet(wb, Sample("E3G_MEDICIONES_TUBERIAS_SANEAMIENTO", "uid-1"));
                ScheduleEditableWorkbook.WriteSheet(wb, Sample("E3G_MEDICIONES_TUBERIAS_SANEAMIENTO_2", "uid-2"));
                var snap = new ScheduleSnapshot { Name = "Resumen" };
                snap.ColumnWidthsMm.Add(30);
                ScheduleExcelWriter.WriteSheet(wb, snap);
                wb.SaveAs(path);
            }

            using (var wb = new XLWorkbook(path))
            {
                var visible = wb.Worksheets.Where(w => w.Visibility == XLWorksheetVisibility.Visible).Select(w => w.Name).ToList();
                visible.Should().HaveCount(3);
                visible.Should().OnlyHaveUniqueItems();
                visible.All(n => n.Length <= 31).Should().BeTrue();
            }

            var file = ScheduleEditableWorkbook.Read(path);
            file.Sheets.Select(s => s.ScheduleUniqueId).Should().Equal("uid-1", "uid-2");
            file.PresentationSheets.Should().Equal("Resumen");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("Sí", true)]
    [InlineData("si", true)]
    [InlineData("X", true)]
    [InlineData("No", false)]
    [InlineData("", false)]
    [InlineData("quizá", null)]
    public void ParseYesNo_AcceptsCommonWords(string text, bool? expected)
    {
        ScheduleImporter.ParseYesNo(text).Should().Be(expected);
    }
}
