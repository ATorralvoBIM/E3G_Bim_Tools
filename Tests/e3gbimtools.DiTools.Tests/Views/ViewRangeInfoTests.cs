using E3G.BimTools.DiTools.Core.Models.Views;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Views;

/// <summary>
/// Validacion de la regla de consistencia top &gt; cut plane &gt; bottom y
/// view depth &le; bottom.  Es la ultima frontera antes de tocar Revit.
/// </summary>
public sealed class ViewRangeInfoTests
{
    [Fact]
    public void Top_mayor_que_cut_plane_mayor_que_bottom_es_consistente()
    {
        var info = new ViewRangeInfo(
            topLevelId: null,        topOffset:        10,
            cutPlaneOffset:                            4,
            bottomLevelId: null,     bottomOffset:    -2,
            viewDepthLevelId: null,  viewDepthOffset: -5);

        info.IsConsistent.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 5, -2, -3)]   // top < cut plane
    [InlineData(10, -1, 2, -5)]  // cut plane < bottom
    [InlineData(10, 4, -2, 0)]   // view depth > bottom
    public void Reglas_violadas_marcan_inconsistente(double top, double cut, double bottom, double depth)
    {
        var info = new ViewRangeInfo(
            topLevelId: null,        topOffset:        top,
            cutPlaneOffset:                            cut,
            bottomLevelId: null,     bottomOffset:    bottom,
            viewDepthLevelId: null,  viewDepthOffset: depth);

        info.IsConsistent.Should().BeFalse();
    }
}
