using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;
using E3G.BimTools.DiTools.Core.Services.ScheduleExcel;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.ScheduleExcel;

/// <summary>Excel «como en Revit» reimportable: mapa de filas, originales y deteccion de cambios.</summary>
public class PresentationImportTests
{
    private static ScheduleSnapshot GroupedSample()
    {
        var snap = new ScheduleSnapshot { Name = "E3G_MEDICIONES_TUBERIAS_SANEAMIENTO", ScheduleUniqueId = "uid-9" };
        snap.TitleLines.Add(snap.Name);
        snap.ColumnWidthsMm.AddRange(new[] { 55.3, 25.4, 30.7, 25.4 });
        snap.HeadingRows.Add(new[] { "Familia y tipo", "Diámetro", "Longitud", "E3G_Codi_PO" }
            .Select(t => new ScheduleCell { Text = t }).ToList());
        snap.ColumnFields.Add(new EditableColumn { Heading = "Familia y tipo", ParameterId = -1002052, Kind = EditableKind.ElementRef });
        snap.ColumnFields.Add(new EditableColumn { Heading = "Diámetro", ParameterId = -1140225, Kind = EditableKind.Number, Editable = true });
        snap.ColumnFields.Add(new EditableColumn { Heading = "Longitud", ParameterId = -1004005, Kind = EditableKind.Number });
        snap.ColumnFields.Add(new EditableColumn { Heading = "E3G_Codi_PO", ParameterId = 18213206, Kind = EditableKind.Text, Editable = true });

        snap.Rows.Add(Group("10_10_Fecals", 1));
        snap.Rows.Add(Group("Tipos de tubería: PIPE_PP_TRICAPA", 2));
        snap.Rows.Add(Data(new long[] { 1, 2, 3 }, "Tipos de tubería: PIPE_PP_TRICAPA", "40", "382,26", ""));
        snap.Rows.Add(Data(Enumerable.Range(10, 6000).Select(i => (long)i).ToArray(), "Tipos de tubería: PIPE_PP_TRICAPA", "50", "220,37", "PO-7"));
        return snap;
    }

    private static ScheduleRow Group(string text, int level)
    {
        var r = new ScheduleRow { Kind = ScheduleRowKind.Group, Level = level };
        r.Cells.Add(new ScheduleCell { Text = text, ColSpan = 4 });
        return r;
    }

    private static ScheduleRow Data(long[] ids, params string[] texts)
    {
        var r = new ScheduleRow { Kind = ScheduleRowKind.Data, ElementIds = ids.ToList(), Label = texts[0] + " › " + texts[1] };
        foreach (var t in texts) r.Cells.Add(new ScheduleCell { Text = t });
        return r;
    }

    [Fact]
    public void PresentationExport_IsImportable_AndKeepsRowMap()
    {
        var path = Path.Combine(Path.GetTempPath(), "e3g_pres_" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ScheduleExcelWriter.Write(GroupedSample(), path);

            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet(1);
                ws.Cell(5, 4).Value = "XXXXX";   // fila Ø40: código nuevo
                ws.Cell(6, 3).Value = 999;       // total tocado sin querer (no editable)
                wb.Save();
            }

            var file = ScheduleEditableWorkbook.Read(path);
            file.Sheets.Should().BeEmpty();
            file.PresentationSheetsData.Should().HaveCount(1);
            var sheet = file.PresentationSheetsData[0];
            sheet.ScheduleUniqueId.Should().Be("uid-9");
            sheet.Rows.Should().HaveCount(2);

            var r40 = sheet.Rows[0];
            r40.ExcelRow.Should().Be(5);
            r40.ElementIds.Should().Equal(1, 2, 3);
            r40.Originals.Should().Equal("Tipos de tubería: PIPE_PP_TRICAPA", "40", "382,26", "");
            ScheduleImporter.ChangedFromOriginal(r40.Values[3], r40.Originals[3], sheet).Should().BeTrue();
            ScheduleImporter.ChangedFromOriginal(r40.Values[1], r40.Originals[1], sheet).Should().BeFalse();   // 40 == "40"

            var r50 = sheet.Rows[1];
            r50.ElementIds.Should().HaveCount(6000);                     // ids troceados en varias celdas
            ScheduleImporter.ChangedFromOriginal(r50.Values[3], r50.Originals[3], sheet).Should().BeFalse();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ChangedFromOriginal_EmptyCell_IsChangeOnlyIfItHadValue()
    {
        var sheet = new ParsedPresentationSheet();
        ScheduleImporter.ChangedFromOriginal(null, "PO-7", sheet).Should().BeTrue();     // el usuario la ha borrado
        ScheduleImporter.ChangedFromOriginal(null, "", sheet).Should().BeFalse();        // ya estaba vacía («varía»)
        ScheduleImporter.ChangedFromOriginal(null, "  ", sheet).Should().BeFalse();
        ScheduleImporter.ChangedFromOriginal("  PO-7 ", "PO-7", sheet).Should().BeFalse();
        ScheduleImporter.ChangedFromOriginal(1234.5, "1.234,5", sheet).Should().BeFalse();
        ScheduleImporter.ChangedFromOriginal(1234.6, "1.234,5", sheet).Should().BeTrue();
    }

    [Fact]
    public void LegacyPresentationWorkbook_ExplainsItMustBeReexported()
    {
        var path = Path.Combine(Path.GetTempPath(), "e3g_pres_" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            var snap = GroupedSample();
            snap.ColumnFields.Clear();                // como una exportación sin mapa de filas
            ScheduleExcelWriter.Write(snap, path);

            var act = () => ScheduleEditableWorkbook.Read(path);
            act.Should().Throw<InvalidOperationException>().WithMessage("*versión anterior*");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
