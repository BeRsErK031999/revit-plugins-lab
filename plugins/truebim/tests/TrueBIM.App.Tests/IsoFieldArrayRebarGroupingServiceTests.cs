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
        Assert.Equal("As1X:zone-a:c0:r0:b0:array", array.StableId);
        Assert.Equal(3, array.SourceStableIds.Count);
    }

    [Fact]
    public void BuildArrays_KeepsDifferentBarSpansAsSeparateArrays()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0),
            CreatePlacement(1, 1),
            CreatePlacement(2, 2, endX: 4),
            CreatePlacement(3, 3, endX: 4)
        ]);

        Assert.Equal(2, arrays.Count);
        Assert.All(arrays, array => Assert.Equal(2, array.BarCount));
        Assert.All(arrays, array => Assert.Equal(1, array.ArrayWidthFeet, precision: 6));
        IsoFieldArrayRebarPlacement longBars = Assert.Single(arrays, array => Math.Abs(array.BarLengthFeet - 5) < 1e-6);
        IsoFieldArrayRebarPlacement shortBars = Assert.Single(arrays, array => Math.Abs(array.BarLengthFeet - 4) < 1e-6);
        Assert.Equal(["As1X:zone-a:c0:r0:b0", "As1X:zone-a:c0:r0:b1"], longBars.SourceStableIds);
        Assert.Equal(["As1X:zone-a:c0:r0:b2", "As1X:zone-a:c0:r0:b3"], shortBars.SourceStableIds);
        Assert.NotEqual(longBars.StableId, shortBars.StableId);
    }

    [Fact]
    public void BuildArrays_SplitsAtMissingScanLineInsteadOfAddingABar()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0),
            CreatePlacement(1, 1),
            CreatePlacement(3, 3),
            CreatePlacement(4, 4)
        ]);

        Assert.Equal(2, arrays.Count);
        Assert.Equal(4, arrays.Sum(array => array.BarCount));
        Assert.All(arrays, array => Assert.Equal(1, array.ArrayWidthFeet, precision: 6));
        Assert.DoesNotContain(arrays.SelectMany(array => array.SourceStableIds), id => id.EndsWith(":b2", StringComparison.Ordinal));
        Assert.Equal(2, arrays.Select(array => array.StableId).Distinct().Count());
    }

    [Fact]
    public void BuildArrays_KeepsDifferentZonesAsSeparateFamilyInstances()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0),
            CreatePlacement(1, 1),
            CreatePlacement(0, 0, zoneId: "zone-b"),
            CreatePlacement(1, 1, zoneId: "zone-b")
        ]);

        Assert.Equal(2, arrays.Count);
        Assert.Contains(arrays, array => array.ZoneId == "zone-a");
        Assert.Contains(arrays, array => array.ZoneId == "zone-b");
    }

    [Fact]
    public void BuildArrays_KeepsDifferentZoneEnvelopesIndependent()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0, endX: 4),
            CreatePlacement(1, 1, endX: 4),
            CreatePlacement(0, 0, endX: 6, zoneId: "zone-b"),
            CreatePlacement(1, 1, endX: 6, zoneId: "zone-b")
        ]);

        Assert.Equal(2, arrays.Count);
        Assert.Contains(arrays, array => array.ZoneId == "zone-a" && Math.Abs(array.BarLengthFeet - 4) < 1e-6);
        Assert.Contains(arrays, array => array.ZoneId == "zone-b" && Math.Abs(array.BarLengthFeet - 6) < 1e-6);
    }

    [Fact]
    public void BuildArrays_RejectsSingleBarInsteadOfSilentlyCreatingSecondBar()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.BuildArrays([CreatePlacement(0, 0)]));

        Assert.Contains("одиночный стержень", exception.Message, StringComparison.Ordinal);
        Assert.Contains("минимум два", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArrays_KeepsCombinationComponentsSeparate()
    {
        IsoFieldRebarComponent secondComponent = new(16, 304.8, 1, 2);
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0),
            CreatePlacement(1, 1),
            CreatePlacement(0.5, 0, component: secondComponent),
            CreatePlacement(1.5, 1, component: secondComponent)
        ]);

        Assert.Equal(2, arrays.Count);
        Assert.Contains(arrays, array => array.Component.CombinationIndex == 0);
        Assert.Contains(arrays, array => array.Component.CombinationIndex == 1);
    }

    [Fact]
    public void BuildArrays_KeepsTwoSidesOfOpeningAsSeparateArrays()
    {
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays = service.BuildArrays(
        [
            CreatePlacement(0, 0, endX: 2),
            CreatePlacement(1, 1, endX: 2),
            CreatePlacement(0, 2, startX: 3, endX: 5),
            CreatePlacement(1, 3, startX: 3, endX: 5)
        ]);

        Assert.Equal(2, arrays.Count);
        Assert.All(arrays, array => Assert.Equal(2, array.BarLengthFeet, precision: 6));
        Assert.All(arrays, array => Assert.Equal(2, array.BarCount));
        Assert.DoesNotContain(arrays, array => array.FirstBarStart.XFeet < 2 && array.FirstBarEnd.XFeet > 3);
    }

    private static IsoFieldRebarPlacement CreatePlacement(
        double y,
        int barIndex,
        double endX = 5,
        string zoneId = "zone-a",
        double startX = 0,
        IsoFieldRebarComponent? component = null)
    {
        component ??= new IsoFieldRebarComponent(12, 304.8, 0, 1);
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
            zoneId,
            "Zone " + zoneId,
            rule,
            new IsoFieldRebarPoint3D(startX, y, 1),
            new IsoFieldRebarPoint3D(endX, y, 1),
            new IsoFieldRebarPoint3D(0, 0, 1),
            component,
            $"As1X:{zoneId}:c{component.CombinationIndex}:r0:b{barIndex}");
    }
}
