using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldSlabBindingServiceTests
{
    private readonly IsoFieldSlabBindingService service = new();

    [Fact]
    public void BuildTransform_MapsBothControlPointsAndCalculatesRotation()
    {
        IsoFieldSlabBindingInput input = new(
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(10, 0),
            new IsoFieldPoint(1, 2),
            new IsoFieldPoint(1, 12),
            MirrorImageY: false);

        IsoFieldPlanarTransform transform = service.BuildTransform(input);

        AssertPoint(new IsoFieldPoint(1, 2), transform.Map(input.ImagePoint1));
        AssertPoint(new IsoFieldPoint(1, 12), transform.Map(input.ImagePoint2));
        Assert.Equal(1, transform.FeetPerPixel, 8);
        Assert.Equal(90, transform.RotationDegrees, 8);
    }

    [Fact]
    public void BuildTransform_MirrorsImageYWhenRequested()
    {
        IsoFieldPlanarTransform transform = service.BuildTransform(new IsoFieldSlabBindingInput(
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(10, 0),
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(10, 0),
            MirrorImageY: true));

        AssertPoint(new IsoFieldPoint(0, -2), transform.Map(new IsoFieldPoint(0, 2)));
    }

    [Fact]
    public void Analyze_AllowsZonesInsideSimpleSlab()
    {
        IsoFieldRecognitionResult recognition = CreateRecognition(
            CreateZone("inside", 25, 25, 75, 75));

        IsoFieldSlabBindingAnalysis analysis = service.Analyze(
            recognition,
            CreateGeometry(includeHole: false),
            CreateDefaultInput());

        Assert.True(analysis.CanProceed);
        Assert.Equal(0, analysis.OutsideZoneCount);
        Assert.Equal(1, analysis.InsideSampleRatio, 8);
        Assert.Single(analysis.MappedZones);
        Assert.Contains(analysis.Diagnostics, message => message.Contains("read-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_BlocksZoneOutsideSlab()
    {
        IsoFieldRecognitionResult recognition = CreateRecognition(
            CreateZone("outside", 75, 25, 125, 75));

        IsoFieldSlabBindingAnalysis analysis = service.Analyze(
            recognition,
            CreateGeometry(includeHole: false),
            CreateDefaultInput());

        Assert.False(analysis.CanProceed);
        Assert.Equal(1, analysis.OutsideZoneCount);
        Assert.Equal(["outside"], analysis.OutsideZoneIds);
        Assert.InRange(analysis.InsideSampleRatio, 0, 0.99);
        Assert.Contains(analysis.Diagnostics, message => message.Contains("За контур", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsHoleAsExcludedArea()
    {
        IsoFieldRecognitionResult recognition = CreateRecognition(
            CreateZone("over-hole", 45, 45, 55, 55));

        IsoFieldSlabBindingAnalysis analysis = service.Analyze(
            recognition,
            CreateGeometry(includeHole: true),
            CreateDefaultInput());

        Assert.False(analysis.CanProceed);
        Assert.Single(analysis.HoleBoundariesFeet);
        Assert.Equal(1, analysis.OutsideZoneCount);
        Assert.Equal(["over-hole"], analysis.OutsideZoneIds);
    }

    [Fact]
    public void Analyze_BlocksZoneThatFullyEnclosesHole()
    {
        IsoFieldRecognitionResult recognition = CreateRecognition(
            CreateZone("encloses-hole", 20, 20, 80, 80));

        IsoFieldSlabBindingAnalysis analysis = service.Analyze(
            recognition,
            CreateGeometry(includeHole: true),
            CreateDefaultInput());

        Assert.False(analysis.CanProceed);
        Assert.Equal(1, analysis.OutsideZoneCount);
    }

    [Fact]
    public void OverlayLayout_ContainsHostZonesAndControlPointsInsideCanvas()
    {
        IsoFieldSlabBindingAnalysis analysis = service.Analyze(
            CreateRecognition(CreateZone("inside", 25, 25, 75, 75)),
            CreateGeometry(includeHole: false),
            CreateDefaultInput());

        IsoFieldSlabOverlayLayout layout = new IsoFieldSlabOverlayLayoutService().Build(
            analysis,
            width: 430,
            height: 180);

        Assert.NotEmpty(layout.OuterBoundary);
        Assert.Single(layout.Zones);
        Assert.Equal(2, layout.ControlPoints.Count);
        Assert.All(
            layout.OuterBoundary
                .Concat(layout.Zones.SelectMany(zone => zone.Points))
                .Concat(layout.ControlPoints),
            point =>
            {
                Assert.InRange(point.X, 0, layout.Width);
                Assert.InRange(point.Y, 0, layout.Height);
            });
    }

    [Fact]
    public void BuildTransform_RejectsCoincidentImagePoints()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.BuildTransform(new IsoFieldSlabBindingInput(
                new IsoFieldPoint(10, 10),
                new IsoFieldPoint(10, 10),
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(1, 0),
                MirrorImageY: false)));

        Assert.Contains("минимум на 1 пиксель", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IsoFieldSlabBindingInput CreateDefaultInput()
    {
        return new IsoFieldSlabBindingInput(
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(100, 0),
            new IsoFieldPoint(-5, -5),
            new IsoFieldPoint(5, -5),
            MirrorImageY: false);
    }

    private static IsoFieldHostGeometry CreateGeometry(bool includeHole)
    {
        List<IReadOnlyList<IsoFieldPoint>> loops =
        [
            CreateLoop(-5, -5, 5, 5)
        ];
        if (includeHole)
        {
            loops.Add(CreateLoop(-1, -1, 1, 1));
        }

        return new IsoFieldHostGeometry(
            new IsoFieldRebarPoint3D(0, 0, 0),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 1, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            loops);
    }

    private static IReadOnlyList<IsoFieldPoint> CreateLoop(
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        return
        [
            new IsoFieldPoint(minX, minY),
            new IsoFieldPoint(maxX, minY),
            new IsoFieldPoint(maxX, maxY),
            new IsoFieldPoint(minX, maxY),
            new IsoFieldPoint(minX, minY)
        ];
    }

    private static IsoFieldRecognitionResult CreateRecognition(params IsoFieldPolyline[] zones)
    {
        return new IsoFieldRecognitionResult(zones, Array.Empty<string>());
    }

    private static IsoFieldPolyline CreateZone(
        string id,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        return new IsoFieldPolyline(
            id,
            CreateLoop(minX, minY, maxX, maxY),
            id,
            0.95,
            IsoFieldLayerRole.As1X);
    }

    private static void AssertPoint(IsoFieldPoint expected, IsoFieldPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
