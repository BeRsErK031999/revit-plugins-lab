using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldArrayRebarGroupingServiceTests
{
    private readonly IsoFieldArrayRebarGroupingService service = new();

    [Fact]
    public void BuildArrays_UsesOneFamilyInstanceForWholeZone()
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
    public void BuildArrays_UsesConservativeZoneEnvelopeWhenBarLengthsChange()
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
    public void BuildArrays_FillsZoneEnvelopeAcrossMissingScanLine()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0),
            CreatePlacement(2, 2)
        ]);

        IsoFieldArrayRebarPlacement array = Assert.Single(arrays);
        Assert.Equal(3, array.BarCount);
        Assert.Equal(2, array.ArrayWidthFeet, precision: 6);
    }

    [Fact]
    public void BuildArrays_KeepsDifferentZonesAsSeparateFamilyInstances()
    {
        IsoFieldRebarPlacement first = CreatePlacement(0, 0);
        IsoFieldRebarPlacement duplicate = first with
        {
            ZoneId = "zone-b",
            ZoneName = "Zone B",
            StableId = "As1X:zone-b:c0:r0:b0"
        };

        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays([first, duplicate]);

        Assert.Equal(2, arrays.Count);
        Assert.Contains(arrays, array => array.ZoneId == "zone-a");
        Assert.Contains(arrays, array => array.ZoneId == "zone-b");
    }

    [Fact]
    public void BuildArrays_KeepsDifferentZoneEnvelopesIndependent()
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

        Assert.Equal(2, arrays.Count);
        Assert.Contains(arrays, array => array.ZoneId == "zone-a" && Math.Abs(array.BarLengthFeet - 4) < 1e-6);
        Assert.Contains(arrays, array => array.ZoneId == "zone-b" && Math.Abs(array.BarLengthFeet - 6) < 1e-6);
    }

    [Fact]
    public void BuildArrays_UsesZeroWidthForSingleBarZone()
    {
        IsoFieldArrayRebarPlacement array = Assert.Single(service.BuildArrays(
        [
            CreatePlacement(0, 0)
        ]));

        Assert.Equal(1, array.BarCount);
        Assert.Equal(0, array.ArrayWidthFeet, precision: 6);
    }

    [Fact]
    public void BuildArrays_KeepsCombinationComponentsSeparate()
    {
        IsoFieldRebarPlacement first = CreatePlacement(0, 0);
        IsoFieldRebarComponent secondComponent = new(16, 304.8, 1, 2);
        IsoFieldRebarPlacement second = first with
        {
            Component = secondComponent,
            StableId = "As1X:zone-a:c1:r0:b0"
        };

        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays([first, second]);

        Assert.Equal(2, arrays.Count);
        Assert.Contains(arrays, array => array.Component.CombinationIndex == 0);
        Assert.Contains(arrays, array => array.Component.CombinationIndex == 1);
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
