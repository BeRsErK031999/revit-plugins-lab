using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldPreviewLayoutServiceTests
{
    [Fact]
    public void Build_ReturnsEmptyLayoutForEmptyRecognitionResult()
    {
        IsoFieldPreviewLayout layout = new IsoFieldPreviewLayoutService().Build(
            IsoFieldRecognitionResult.Empty,
            width: 200,
            height: 100);

        Assert.Empty(layout.Polylines);
        Assert.Equal(200, layout.Width);
        Assert.Equal(100, layout.Height);
    }

    [Fact]
    public void Build_ScalesPolylinePointsInsidePreviewBounds()
    {
        IsoFieldRecognitionResult result = new(
            [
                new IsoFieldPolyline(
                    "zone-a",
                    [
                        new IsoFieldPoint(100, 50),
                        new IsoFieldPoint(300, 50),
                        new IsoFieldPoint(300, 150),
                        new IsoFieldPoint(100, 150)
                    ],
                    "Zone A",
                    0.9)
            ],
            Array.Empty<string>());

        IsoFieldPreviewLayout layout = new IsoFieldPreviewLayoutService().Build(
            result,
            width: 220,
            height: 120);

        IsoFieldPreviewPolyline polyline = Assert.Single(layout.Polylines);
        Assert.Equal("zone-a", polyline.Id);
        Assert.Equal("Zone A", polyline.ZoneName);
        Assert.Equal(0.9, polyline.Confidence);
        Assert.All(polyline.Points, point =>
        {
            Assert.InRange(point.X, 0, 220);
            Assert.InRange(point.Y, 0, 120);
        });
    }
}
