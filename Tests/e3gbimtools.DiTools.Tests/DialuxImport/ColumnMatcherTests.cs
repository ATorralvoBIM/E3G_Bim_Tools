using System;
using System.Linq;
using E3G.BimTools.DiTools.Core.Services.DialuxImport;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.DialuxImport;

public class ColumnMatcherTests
{
    // Pilares de la planta baja del hotel de Sant Cugat (25-144): DWG de DIALux y modelo ARQ (origen interno), en m.
    private static readonly (double X, double Y)[] Dwg = Parse(
        "3.63;21.423 3.63;28.821 3.63;36.247 3.63;43.673 3.63;51.071 5.344;53.281 5.344;58.981 8.08;21.423 8.08;28.821 8.08;36.247 " +
        "8.08;43.673 8.08;51.071 11.254;55.466 11.254;58.981 12.33;48.843 14.23;48.843 15.34;23.453 15.34;29.363 15.34;35.301 15.34;41.921 " +
        "17.03;45.321 17.03;49.249 17.192;55.466 17.192;58.981 18.44;23.453 18.44;29.363 18.44;35.301 18.44;41.921 23.13;45.321 23.13;49.249 " +
        "23.13;55.466 23.13;58.981 29.068;45.321 29.068;49.249 29.068;55.466 29.068;58.981 35.006;45.321 35.006;49.249 35.006;55.466 35.006;58.981 " +
        "38.376;22.503 38.376;27.503 40.944;45.321 40.944;49.606 40.944;55.466 40.944;58.981 43.176;22.503 43.176;45.244 46.882;55.466 46.882;58.981 " +
        "47.241;45.889 47.241;49.781 47.977;22.503 52.82;55.466 52.82;58.981 53.201;22.503 53.201;45.289 53.201;51.199 56.776;22.503 56.776;39.351 " +
        "56.776;45.289 56.776;51.199 58.73;53.281 58.73;58.981");

    private static readonly (double X, double Y)[] Revit = Parse(
        "-76.786;8.433 -76.786;15.831 -76.786;23.257 -76.786;30.683 -76.786;38.081 -75.072;40.638 -75.072;45.991 -72.356;8.433 -72.356;15.831 " +
        "-72.356;23.257 -72.356;30.683 -72.356;38.081 -69.162;42.476 -69.162;45.991 -68.086;35.691 -66.186;35.691 -65.076;10.463 -65.076;16.373 " +
        "-65.076;22.311 -65.076;28.931 -63.341;32.331 -63.341;36.259 -63.224;42.476 -63.224;45.991 -61.976;10.463 -61.976;16.373 -61.976;22.311 " +
        "-61.976;28.931 -57.286;32.331 -57.286;36.259 -57.286;42.476 -57.286;45.991 -51.513;32.331 -51.513;36.259 -51.513;42.476 -51.513;45.991 " +
        "-51.183;32.331 -51.183;36.259 -51.183;42.476 -51.183;45.991 -45.41;32.331 -45.41;36.259 -45.41;42.476 -45.41;45.991 -42.04;9.513 " +
        "-42.04;14.513 -42.04;20.423 -42.04;26.361 -39.472;32.331 -39.472;36.616 -39.472;42.476 -39.472;45.991 -37.24;9.513 -37.24;14.513 " +
        "-37.24;20.423 -37.24;26.361 -37.24;32.255 -33.534;42.476 -33.534;45.991 -32.69;32.991 -32.69;36.791 -32.44;20.423 -32.439;9.513 " +
        "-32.439;14.513 -32.439;26.361 -27.596;42.476 -27.596;45.991 -27.215;9.513 -27.215;14.513 -27.215;20.423 -27.215;26.361 -27.215;32.299 " +
        "-27.215;38.209 -23.64;9.513 -23.64;14.513 -23.64;20.423 -23.64;26.361 -23.64;32.299 -23.64;38.209 -21.686;40.291 -21.686;45.991");

    [Fact]
    public void Match_RealHotelColumns_FindsPureTranslation()
    {
        var r = ColumnMatcher.Match(Dwg, Revit, 0.10);

        r.Should().NotBeNull();
        r!.Value.RotationRad.Should().BeApproximately(0, 1e-6);
        r.Value.Tx.Should().BeApproximately(-80.416, 0.01);
        r.Value.Ty.Should().BeApproximately(-12.990, 0.01);
        r.Value.Matched.Should().BeGreaterThan(Dwg.Length / 3);
    }

    [Fact]
    public void Match_RotatedAndShiftedSubset_RecoversTransform()
    {
        double theta = 17.5 * Math.PI / 180, tx = 120.3, ty = -45.7;
        double c = Math.Cos(theta), s = Math.Sin(theta);
        // Revit tiene todos los pilares; el DWG solo una parte, con algo de ruido.
        var rnd = new Random(7);
        var dwg = Dwg.Where((_, i) => i % 3 != 0).ToArray();
        var revit = Dwg.Select(p => (X: c * p.X - s * p.Y + tx + (rnd.NextDouble() - 0.5) * 0.01,
                                     Y: s * p.X + c * p.Y + ty + (rnd.NextDouble() - 0.5) * 0.01)).ToArray();

        var r = ColumnMatcher.Match(dwg, revit, 0.10);

        r.Should().NotBeNull();
        r!.Value.RotationRad.Should().BeApproximately(theta, 0.2 * Math.PI / 180);
        r.Value.Matched.Should().BeGreaterThan(dwg.Length * 9 / 10);
        // Comprobacion por puntos: el error medio es del orden del ruido.
        r.Value.MeanError.Should().BeLessThan(0.05);
    }

    private static (double X, double Y)[] Parse(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(';'))
            .Select(p => (double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                          double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
}
