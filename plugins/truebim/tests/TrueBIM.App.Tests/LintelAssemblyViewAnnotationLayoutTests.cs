using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelAssemblyViewAnnotationLayoutTests
{
    private const double FeetPerMillimeter = 1.0 / 304.8;

    [Fact]
    public void Create_UsesTemporaryMinimumFrameAndPaperDimensionOffset()
    {
        LintelViewProjectedBounds bounds = new(
            -250 * FeetPerMillimeter,
            250 * FeetPerMillimeter,
            0,
            90 * FeetPerMillimeter);

        LintelAssemblyViewAnnotationLayout layout = LintelAssemblyViewAnnotationLayout.Create(bounds, 10);

        Assert.Equal(1050 * FeetPerMillimeter, layout.FrameWidth, 8);
        Assert.Equal(385 * FeetPerMillimeter, layout.FrameHeight, 8);
        Assert.Equal(-50 * FeetPerMillimeter, layout.DimensionVertical, 8);
    }

    [Fact]
    public void Create_ExpandsFrameWhenGeometryDoesNotFitMinimum()
    {
        LintelViewProjectedBounds bounds = new(
            0,
            1400 * FeetPerMillimeter,
            0,
            500 * FeetPerMillimeter);

        LintelAssemblyViewAnnotationLayout layout = LintelAssemblyViewAnnotationLayout.Create(bounds, 10);

        Assert.True(layout.FrameWidth > 1400 * FeetPerMillimeter);
        Assert.True(layout.FrameHeight > 500 * FeetPerMillimeter);
        Assert.InRange(bounds.CenterHorizontal, layout.FrameMinHorizontal, layout.FrameMaxHorizontal);
        Assert.InRange(bounds.CenterVertical, layout.FrameMinVertical, layout.FrameMaxVertical);
    }

    [Fact]
    public void CreateFrameSegments_ReturnsClosedRectangleAtFrameBounds()
    {
        LintelViewProjectedBounds bounds = new(
            -510 * FeetPerMillimeter,
            510 * FeetPerMillimeter,
            0,
            90 * FeetPerMillimeter);
        LintelAssemblyViewAnnotationLayout layout = LintelAssemblyViewAnnotationLayout.Create(bounds, 10);

        IReadOnlyList<LintelViewSegment> segments = layout.CreateFrameSegments();

        Assert.Equal(4, segments.Count);
        Assert.Equal(new LintelViewPoint(layout.FrameMinHorizontal, layout.FrameMinVertical), segments[0].Start);
        Assert.Equal(new LintelViewPoint(layout.FrameMaxHorizontal, layout.FrameMinVertical), segments[0].End);
        for (int index = 0; index < segments.Count; index++)
        {
            Assert.Equal(segments[index].End, segments[(index + 1) % segments.Count].Start);
        }
    }
}
