using System.IO;
using System.Linq;
using FluentAssertions;
using E3G.BimTools.DiTools.Core.Models.TableGen;
using E3G.BimTools.DiTools.Core.Services.TableGen;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.TableGen;

/// <summary>
/// Tests sobre <see cref="ExcelTableReader"/> con las tres tablas de muestra
/// (Sprint 8 — criterios de aceptacion).
///
/// Estos tests cubren solo la capa de lectura de Excel (sin Revit),
/// por lo que pueden ejecutarse en cualquier maquina sin Revit instalado.
/// </summary>
public sealed class TableGenReaderTests
{
    private static readonly string SamplesDir =
        Path.Combine(
            Path.GetDirectoryName(typeof(TableGenReaderTests).Assembly.Location)!,
            "TableGen", "SampleData");

    private static ExcelTableReader Reader => new();

    // ─────────────────────────────────────────────────────────────────────
    // Tabla 1: simple_table.xlsx — 20 filas x 10 columnas
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void SimpleTable_RowAndColumnCount_IsCorrect()
    {
        var data = ReadSample("simple_table.xlsx", "Simple");

        data.RowCount.Should().Be(21,
            "la hoja tiene 1 cabecera + 20 filas de datos");
        data.ColumnCount.Should().Be(10,
            "la hoja tiene 10 columnas");
    }

    [Fact]
    public void SimpleTable_HeaderRow_IsBoldAndHasBackground()
    {
        var data    = ReadSample("simple_table.xlsx", "Simple");
        var headers = data.Cells.Where(c => c.Row == 1).ToList();

        headers.Should().HaveCount(10, "hay 10 columnas de cabecera");
        headers.Should().OnlyContain(c => c.Bold,
            "todas las celdas de la cabecera son Bold");
        headers.Should().OnlyContain(c => c.HasBackground,
            "todas las celdas de la cabecera tienen fondo de color");
    }

    [Fact]
    public void SimpleTable_DataCells_HaveExpectedValues()
    {
        var data = ReadSample("simple_table.xlsx", "Simple");

        // La celda (2,1) deberia contener "R2C1"
        var cell21 = data.Cells.Single(c => c.Row == 2 && c.Column == 1);
        cell21.Value.Should().Be("R2C1");

        // La celda (21,10) deberia contener "R21C10"
        var cellLast = data.Cells.Single(c => c.Row == 21 && c.Column == 10);
        cellLast.Value.Should().Be("R21C10");
    }

    [Fact]
    public void SimpleTable_PhysicalDimensions_ArePositive()
    {
        var data = ReadSample("simple_table.xlsx", "Simple");

        data.ColumnWidthsMm.Values.Should().OnlyContain(w => w > 0,
            "todos los anchos de columna deben ser positivos");
        data.RowHeightsMm.Values.Should().OnlyContain(h => h > 0,
            "todas las alturas de fila deben ser positivas");
    }

    [Fact]
    public void SimpleTable_TotalWidth_IsWithin5Percent_Of_Expected()
    {
        var data = ReadSample("simple_table.xlsx", "Simple");

        // simple_table.xlsx: 10 columnas de 12 character-units ≈ 12 * 2.46 = 29.5 mm c/u
        // Total esperado ≈ 295 mm.  Aceptamos ±5 %.
        double totalWidthMm = data.ColumnWidthsMm.Values.Sum();
        double expectedMm   = 10 * 12 * 2.46;
        double tolerance    = expectedMm * 0.05;

        totalWidthMm.Should().BeApproximately(expectedMm, tolerance,
            "la suma de anchos de columna debe coincidir con el Excel dentro del 5 %");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tabla 2: merged_table.xlsx — celdas combinadas
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void MergedTable_TitleCell_SpansFullWidth()
    {
        var data = ReadSample("merged_table.xlsx", "ConMerges");

        // "A1:E1" = 1 fila, 5 columnas
        var title = data.Cells.SingleOrDefault(c => c.Row == 1 && c.Column == 1);
        title.Should().NotBeNull("la celda del titulo debe existir");
        title!.MergedArea.Should().NotBeNull("la celda del titulo esta combinada");
        title.MergedArea!.ColumnSpan.Should().Be(5,
            "el titulo abarca 5 columnas (A1:E1)");
        title.MergedArea.RowSpan.Should().Be(1);
        title.Value.Should().Be("INFORME ANUAL 2024");
    }

    [Fact]
    public void MergedTable_VerticalMerge_HasCorrectRowSpan()
    {
        var data = ReadSample("merged_table.xlsx", "ConMerges");

        // "E3:E4" = 2 filas, 1 columna
        var totalCell = data.Cells.SingleOrDefault(c => c.Row == 3 && c.Column == 5);
        totalCell.Should().NotBeNull("la celda TOTAL debe existir");
        totalCell!.MergedArea.Should().NotBeNull();
        totalCell.MergedArea!.RowSpan.Should().Be(2,
            "E3:E4 ocupa 2 filas");
        totalCell.MergedArea.ColumnSpan.Should().Be(1);
    }

    [Fact]
    public void MergedTable_CoveredCells_AreNotIncluded()
    {
        var data = ReadSample("merged_table.xlsx", "ConMerges");

        // Las celdas B1, C1, D1, E1 son parte del merge "A1:E1" pero
        // no son la celda de inicio, por lo que no deben aparecer.
        data.Cells.Should().NotContain(c => c.Row == 1 && c.Column == 2,
            "B1 esta cubierta por el merge A1:E1 y no debe incluirse");
        data.Cells.Should().NotContain(c => c.Row == 4 && c.Column == 5,
            "E4 esta cubierta por el merge E3:E4 y no debe incluirse");
    }

    [Fact]
    public void MergedTable_MergedCellWidth_IsAccumulatedCorrectly()
    {
        var data  = ReadSample("merged_table.xlsx", "ConMerges");
        var title = data.Cells.Single(c => c.Row == 1 && c.Column == 1);

        // La celda combinada A1:E1 deberia tener un ancho = suma de 5 columnas
        double sumOf5Cols = Enumerable.Range(1, 5)
            .Sum(c => data.ColumnWidthsMm.TryGetValue(c, out var w) ? w : 25.4);

        title.WidthMm.Should().BeApproximately(sumOf5Cols, 0.1,
            "el ancho de la celda combinada es la suma de sus columnas");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tabla 3: conditional_table.xlsx — formato condicional (ignorable)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConditionalTable_AllCells_AreRead()
    {
        var data = ReadSample("conditional_table.xlsx", "FormatoCondicional");

        // 10 filas x 5 columnas = 50 celdas
        data.Cells.Should().HaveCount(50,
            "la tabla tiene 10 filas x 5 columnas sin merges");
    }

    [Fact]
    public void ConditionalTable_NumericValues_AreReadCorrectly()
    {
        var data = ReadSample("conditional_table.xlsx", "FormatoCondicional");

        // OnlyContain usa Expression<Func<T,bool>>: no admite 'out var'.
        // Evaluamos fuera y comprobamos el booleano resultante.
        bool allNumericInRange = data.Cells.All(c =>
            int.TryParse(c.Value, out int v) && v >= 0 && v <= 100);

        allNumericInRange.Should().BeTrue("todos los valores son enteros entre 0 y 100");
    }

    [Fact]
    public void ConditionalTable_ColoredCells_HaveBackground()
    {
        var data = ReadSample("conditional_table.xlsx", "FormatoCondicional");

        // Todas las celdas tienen fondo de color (rojo, verde o amarillo)
        data.Cells.Should().OnlyContain(c => c.HasBackground,
            "todas las celdas tienen fill de color explicito");
    }

    [Fact]
    public void ConditionalTable_BackgroundColors_AreOneOfThreeValues()
    {
        var data   = ReadSample("conditional_table.xlsx", "FormatoCondicional");
        var colors = data.Cells.Select(c => c.BackgroundColor).Distinct().ToList();

        colors.Should().HaveCountLessOrEqualTo(3,
            "solo hay tres colores de fondo posibles: rojo, verde y amarillo");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tests de GetSheetNames / GetUsedRangeAddress
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSheetNames_ReturnsCorrectNames()
    {
        var reader = Reader;
        var names  = reader.GetSheetNames(SamplePath("simple_table.xlsx"));
        names.Should().Contain("Simple");
    }

    [Fact]
    public void GetUsedRangeAddress_SimpleTable_StartsAtA1()
    {
        var reader = Reader;
        var range  = reader.GetUsedRangeAddress(
            SamplePath("simple_table.xlsx"), "Simple");

        range.Should().StartWith("A1",
            "el rango utilizado empieza en A1");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static ExcelSheetData ReadSample(string fileName, string sheetName)
    {
        var req = new TableGenRequest
        {
            FilePath  = SamplePath(fileName),
            SheetName = sheetName,
        };
        return Reader.Read(req);
    }

    private static string SamplePath(string fileName) =>
        Path.Combine(SamplesDir, fileName);
}
