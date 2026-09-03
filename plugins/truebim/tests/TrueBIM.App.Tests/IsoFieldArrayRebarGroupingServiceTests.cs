using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldArrayRebarGroupingServiceTests
{
    private readonly IsoFieldArrayRebarGroupingService service = new();

    [Fact]
    public void BuildArrays_GroupsConsecutiveEqualBarsIntoOneFamilyInstance()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0),
            CreatePlacement(1, 1),
            CreatePlacement(2, 2)
        ]);

        IsoFieldArrayRebarPlacement array = Assert.Single(arrays);
        Assert.Equal(3, array.BarCount);
        Assert.Equal(5, array.BarLengthFeet, precision: 6);
        Assert.Equal(2, array.ArrayWidthFeet, precision: 6);
        Assert.Equal("As1X:zone-a:c0:r0:a0", array.StableId);
        Assert.Equal(3, array.SourceStableIds.Count);
    }

    [Fact]
    public void BuildArrays_MergesSingletonWithAdjacentRunWhenLengthChanges()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0),
            CreatePlacement(1, 1),
            CreatePlacement(2, 2, endX: 4)
        ]);

        IsoFieldArrayRebarPlacement array = Assert.Single(arrays);
        Assert.Equal(3, array.BarCount);
        Assert.Equal(5, array.BarLengthFeet, precision: 6);
        Assert.Equal(2, array.ArrayWidthFeet, precision: 6);
    }

    [Fact]
    public void BuildArrays_SplitsNonConsecutiveBars()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0),
            CreatePlacement(2, 2)
        ]);

        Assert.Equal(2, arrays.Count);
        Assert.All(arrays, array => Assert.Equal(1, array.BarCount));
        Assert.All(arrays, array => Assert.Equal(1, array.ArrayWidthFeet, precision: 6));
    }

    [Fact]
    public void BuildArrays_CollapsesCoincidentFamiliesFromOverlappingZones()
    {
        IsoFieldRebarPlacement first = CreatePlacement(0, 0);
        IsoFieldRebarPlacement duplicate = first with
        {
            ZoneId = "zone-b",
            ZoneName = "Zone B",
            StableId = "As1X:zone-b:c0:r0:b0"
        };

        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays([first, duplicate]);

        IsoFieldArrayRebarPlacement array = Assert.Single(arrays);
        Assert.Equal(2, array.SourceStableIds.Count);
        Assert.Contains("As1X:zone-a:c0:r0:b0", array.SourceStableIds);
        Assert.Contains("As1X:zone-b:c0:r0:b0", array.SourceStableIds);
    }

    [Fact]
    public void BuildArrays_CollapsesSameOriginFamiliesUsingConservativeEnvelope()
    {
        IsoFieldRebarPlacement first = CreatePlacement(0, 0, endX: 4);
        IsoFieldRebarPlacement overlapping = first with
        {
            ZoneId = "zone-b",
            ZoneName = "Zone B",
            End = new IsoFieldRebarPoint3D(6, 0, 1),
            StableId = "As1X:zone-b:c0:r0:b0"
        };

        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays([first, overlapping]);

        IsoFieldArrayRebarPlacement array = Assert.Single(arrays);
        Assert.Equal(6, array.BarLengthFeet, precision: 6);
        Assert.Equal(2, array.SourceStableIds.Count);
    }

    private static IsoFieldRebarPlacement CreatePlacement(
        double y,
        int barIndex,
        double endX = 5)
    {
        IsoFieldRebarComponent component = new(12, 304.8, 0, 1);
        RebarRule rule = new(
            "rule",
            "Slab",
            component.BarTypeName,
            component.SpacingMillimeters,
            PlacementDirection: "X",
            RequiredAreaSquareCentimetersPerMeter: 5,
            ProvidedAreaSquareCentimetersPerMeter: 6,
            LayerRole: IsoFieldLayerRole.As1X,
            Face: IsoFieldRebarFace.Top,
            Components: [component]);
        return new IsoFieldRebarPlacement(
            "zone-a",
            "Zone A",
            rule,
            new IsoFieldRebarPoint3D(0, y, 1),
            new IsoFieldRebarPoint3D(endX, y, 1),
            new IsoFieldRebarPoint3D(0, 0, 1),
            component,
            $"As1X:zone-a:c0:r0:b{barIndex}");
    }
}
