using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldControlPointSnapServiceTests
{
    private readonly IsoFieldControlPointSnapService service = new();

    [Fact]
    public void SnapToNearestOuterCorner_NearCorner_ReturnsExactVertex()
    {
        IsoFieldPoint result = service.SnapToNearestOuterCorner(
            new IsoFieldPoint(0.5, 0.4),
            CreateGeometry());

        Assert.Equal(new IsoFieldPoint(0, 0), result);
    }

    [Fact]
    public void SnapToNearestOuterCorner_FarFromCorner_RejectsPoint()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.SnapToNearestOuterCorner(
                new IsoFieldPoint(5, 5),
                CreateGeometry()));

        Assert.Contains("ближе к углу", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IsoFieldHostGeometry CreateGeometry()
    {
        IReadOnlyList<IsoFieldPoint> outer =
        [
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(10, 0),
            new IsoFieldPoint(10, 10),
            new IsoFieldPoint(0, 10),
            new IsoFieldPoint(0, 0)
        ];
        return new IsoFieldHostGeometry(
            new IsoFieldRebarPoint3D(0, 0, 0),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 1, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            [outer]);
    }
}
