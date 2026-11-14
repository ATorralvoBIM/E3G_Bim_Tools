using System;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using E3G.BimTools.DiTools.Core.Services.DialuxImport;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.DialuxImport;

public class DialuxDwgReaderTests
{
    [Fact]
    public void Read_TopLevelAndNestedInserts_ReturnsWorldPositionRotationAndAttributes()
    {
        var doc = new CadDocument();
        doc.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Meters;
        var layer = new Layer("LUMINARIAS");
        doc.Layers.Add(layer);

        var lum = new BlockRecord("LUM_A");
        lum.Entities.Add(new Circle { Center = XYZ.Zero, Radius = 0.3 });
        doc.BlockRecords.Add(lum);

        // Grupo girado 90° en (10,0) que contiene una LUM_A en (2,0) local.
        var group = new BlockRecord("GRUPO");
        group.Entities.Add(new Insert(lum) { InsertPoint = new XYZ(2, 0, 0) });
        doc.BlockRecords.Add(group);

        var top = new Insert(lum) { InsertPoint = new XYZ(5, 3, 2.7), Rotation = Math.PI / 2, Layer = layer };
        doc.Entities.Add(top);
        doc.Entities.Add(new Insert(group) { InsertPoint = new XYZ(10, 0, 0), Rotation = Math.PI / 2, Layer = layer });

        var path = Path.Combine(Path.GetTempPath(), $"dialux_test_{Guid.NewGuid():N}.dxf");
        try
        {
            using (var w = new DxfWriter(path, doc, false)) w.Write();

            var d = new DialuxDwgReader().Read(path);

            d.DeclaredUnits.Should().Be(E3G.BimTools.DiTools.Core.Models.DxfLegend.LegendUnits.Meters);
            var lums = d.Inserts.Where(i => i.BlockName == "LUM_A").OrderBy(i => i.Depth).ToList();
            lums.Should().HaveCount(2);

            lums[0].X.Should().BeApproximately(5, 1e-9);
            lums[0].Y.Should().BeApproximately(3, 1e-9);
            lums[0].Z.Should().BeApproximately(2.7, 1e-9);
            lums[0].Rotation.Should().BeApproximately(Math.PI / 2, 1e-9);
            lums[0].Layer.Should().Be("LUMINARIAS");

            // Anidada: (10,0) + R90·(2,0) = (10,2), girada 90°, capa 0 → capa del grupo.
            lums[1].Depth.Should().Be(1);
            lums[1].X.Should().BeApproximately(10, 1e-9);
            lums[1].Y.Should().BeApproximately(2, 1e-9);
            lums[1].Rotation.Should().BeApproximately(Math.PI / 2, 1e-9);
            lums[1].Layer.Should().Be("LUMINARIAS");

            d.Blocks.Should().Contain(b => b.Name == "LUM_A" && b.Count == 2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Formato real de DIALux: cada luminaria es un bloque anonimo propio en la
    /// capa DLXLUM, insertado sin giro, con el simbolo ya girado y un texto con su
    /// numero; la leyenda (capa DLXDESCR) da la descripcion.
    /// </summary>
    [Fact]
    public void Read_DialuxAnonymousLuminaires_GroupsByNumberWithLegendAndSymbolRotation()
    {
        var doc = new CadDocument();
        var lumLayer = new Layer("DLXLUM");
        var descLayer = new Layer("DLXDESCR");
        doc.Layers.Add(lumLayer);
        doc.Layers.Add(descLayer);

        BlockRecord Luminaire(string name, double dx, double dy)
        {
            var b = new BlockRecord(name);
            b.Entities.Add(new Line { StartPoint = XYZ.Zero, EndPoint = new XYZ(dx, dy, 0) });
            b.Entities.Add(new Line { StartPoint = new XYZ(-0.07, -0.03, 0), EndPoint = new XYZ(0.07, -0.03, 0) });
            b.Entities.Add(new TextEntity { Value = "1", Height = 0.1 });
            doc.BlockRecords.Add(b);
            return b;
        }

        doc.Entities.Add(new Insert(Luminaire("*U1", 0, 0.5)) { InsertPoint = new XYZ(18, 52, 0), Layer = lumLayer });
        doc.Entities.Add(new Insert(Luminaire("*U2", -0.5, 0)) { InsertPoint = new XYZ(11, 28, 0), Layer = lumLayer });

        var legend = new BlockRecord("*U9");
        foreach (var t in new[] { "DIALux", "Proyecto", "1", "ELECTROZEMPER SA LYA3150C ALYA 150lm 1h", "Isolíneas", "0.1 lx" })
            legend.Entities.Add(new TextEntity { Value = t, Height = 0.1 });
        doc.BlockRecords.Add(legend);
        doc.Entities.Add(new Insert(legend) { Layer = descLayer });

        var path = Path.Combine(Path.GetTempPath(), $"dialux_anon_{Guid.NewGuid():N}.dxf");
        try
        {
            using (var w = new DxfWriter(path, doc, false)) w.Write();

            var d = new DialuxDwgReader().Read(path);

            d.HasDialuxLuminaires.Should().BeTrue();
            var lums = d.Inserts.Where(i => i.IsDialuxLuminaire).OrderBy(i => i.X).ToList();
            lums.Should().HaveCount(2);
            lums.Should().OnlyContain(i => i.BlockName == "DIALux 1 · ELECTROZEMPER SA LYA3150C ALYA 150lm 1h");
            lums[0].Rotation.Should().BeApproximately(Math.PI, 1e-9);     // (11,28): simbolo hacia -X
            lums[1].Rotation.Should().BeApproximately(Math.PI / 2, 1e-9); // (18,52): simbolo hacia +Y

            var block = d.Blocks.Single(b => b.IsDialuxLuminaire);
            block.Count.Should().Be(2);
            block.Symbol.Should().NotBeEmpty();
            d.Blocks.Should().NotContain(b => b.Name.StartsWith("*"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
