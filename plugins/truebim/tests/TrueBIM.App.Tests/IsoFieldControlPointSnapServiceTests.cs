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

    [Fact]
    public void CreateAutomaticOuterCornerTriad_Rectangle_ReturnsExpectedThreeCorners()
    {
        IsoFieldControlPointTriad? result = service.CreateAutomaticOuterCornerTriad(
            CreateGeometry());

        Assert.NotNull(result);
        Assert.Equal(new IsoFieldPoint(0, 10), result.Point1);
        Assert.Equal(new IsoFieldPoint(10, 10), result.Point2);
        Assert.Equal(new IsoFieldPoint(0, 0), result.Point3);
    }

    [Fact]
    public void CreateAutomaticOuterCornerTriad_TranslatedClockwiseRectangle_IsDeterministic()
    {
        IReadOnlyList<IsoFieldPoint> outer =
        [
            new IsoFieldPoint(17, -3),
            new IsoFieldPoint(17, -11),
            new IsoFieldPoint(-5, -11),
            new IsoFieldPoint(-5, -3),
            new IsoFieldPoint(17, -3)
        ];

        IsoFieldControlPointTriad? result = service.CreateAutomaticOuterCornerTriad(
            CreateGeometry(outer));

        Assert.NotNull(result);
        Assert.Equal(new IsoFieldPoint(-5, -3), result.Point1);
        Assert.Equal(new IsoFieldPoint(17, -3), result.Point2);
        Assert.Equal(new IsoFieldPoint(-5, -11), result.Point3);
    }

    [Fact]
    public void CreateAutomaticOuterCornerTriad_WithHole_UsesOuterBoundary()
    {
        IReadOnlyList<IsoFieldPoint> hole =
        [
            new IsoFieldPoint(4, 4),
            new IsoFieldPoint(6, 4),
            new IsoFieldPoint(6, 6),
            new IsoFieldPoint(4, 6),
            new IsoFieldPoint(4, 4)
        ];

        IsoFieldControlPointTriad? result = service.CreateAutomaticOuterCornerTriad(
            CreateGeometry(CreateOuterBoundary(), hole));

        Assert.NotNull(result);
        Assert.Equal(new IsoFieldPoint(0, 10), result.Point1);
        Assert.Equal(new IsoFieldPoint(10, 10), result.Point2);
        Assert.Equal(new IsoFieldPoint(0, 0), result.Point3);
    }

    [Fact]
    public void CreateAutomaticOuterCornerTriad_LShapedBoundary_ReturnsNull()
    {
        IReadOnlyList<IsoFieldPoint> outer =
        [
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(10, 0),
            new IsoFieldPoint(10, 5),
            new IsoFieldPoint(5, 5),
            new IsoFieldPoint(5, 10),
            new IsoFieldPoint(0, 10),
            new IsoFieldPoint(0, 0)
        ];

        IsoFieldControlPointTriad? result = service.CreateAutomaticOuterCornerTriad(
            CreateGeometry(outer));

        Assert.Null(result);
    }

    private static IsoFieldHostGeometry CreateGeometry()
    {
        return CreateGeometry(CreateOuterBoundary());
    }

    private static IReadOnlyList<IsoFieldPoint> CreateOuterBoundary()
    {
        return
        [
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(10, 0),
            new IsoFieldPoint(10, 10),
            new IsoFieldPoint(0, 10),
            new IsoFieldPoint(0, 0)
        ];
    }

    private static IsoFieldHostGeometry CreateGeometry(
        params IReadOnlyList<IsoFieldPoint>[] boundaries)
    {
        return new IsoFieldHostGeometry(
            new IsoFieldRebarPoint3D(0, 0, 0),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 1, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            boundaries);
    }
}
