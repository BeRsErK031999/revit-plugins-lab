using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Revit;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class SlabRebarPlacementServiceTests
{
    private readonly SlabRebarPlacementService service = new();

    [Fact]
    public void BuildPlacements_UsesLongerSlabAxisForAutoDirection()
    {
        IsoFieldRebarPlacementBounds bounds = new(0, 0, 0, 10, 4, 0.5);
        RebarRulePreviewItem item = CreateItem("zone-a", "Auto", spacingMillimeters: 150);

        IsoFieldRebarPlacement placement = Assert.Single(service.BuildPlacements(bounds, [item]));

        Assert.True(placement.End.XFeet > placement.Start.XFeet);
        Assert.Equal(2, placement.Start.YFeet, precision: 6);
        Assert.Equal(2, placement.End.YFeet, precision: 6);
        Assert.Equal(1, placement.Normal.ZFeet);
    }

    [Fact]
    public void BuildPlacements_CreatesCenteredOffsetsForMultipleZones()
    {
        IsoFieldRebarPlacementBounds bounds = new(0, 0, 0, 10, 8, 0.5);

        IReadOnlyList<IsoFieldRebarPlacement> placements = service.BuildPlacements(
            bounds,
            [
                CreateItem("zone-a", "X", spacingMillimeters: 150),
                CreateItem("zone-b", "X", spacingMillimeters: 150),
                CreateItem("zone-c", "X", spacingMillimeters: 150)
            ]);

        Assert.Equal(3, placements.Count);
        Assert.True(placements[0].Start.YFeet < placements[1].Start.YFeet);
        Assert.True(placements[1].Start.YFeet < placements[2].Start.YFeet);
        Assert.Equal(4, placements[1].Start.YFeet, precision: 6);
        Assert.All(placements, placement => Assert.True(placement.LengthFeet >= 0.5));
    }

    [Fact]
    public void BuildPlacements_CompressesOffsetsInsideNarrowSlab()
    {
        IsoFieldRebarPlacementBounds bounds = new(0, 0, 0, 10, 1, 0.5);

        IReadOnlyList<IsoFieldRebarPlacement> placements = service.BuildPlacements(
            bounds,
            [
                CreateItem("zone-a", "X", spacingMillimeters: 400),
                CreateItem("zone-b", "X", spacingMillimeters: 400),
                CreateItem("zone-c", "X", spacingMillimeters: 400),
                CreateItem("zone-d", "X", spacingMillimeters: 400)
            ]);

        Assert.All(placements, placement =>
        {
            Assert.InRange(placement.Start.YFeet, bounds.MinYFeet, bounds.MaxYFeet);
            Assert.InRange(placement.End.YFeet, bounds.MinYFeet, bounds.MaxYFeet);
        });
    }

    [Fact]
    public void BuildPlacements_RejectsNonSlabRules()
    {
        IsoFieldRebarPlacementBounds bounds = new(0, 0, 0, 10, 8, 0.5);
        RebarRulePreviewItem wallItem = new(
            "zone-a",
            "Zone A",
            new RebarRule("Rule A", "Wall", "Ø12 A500", 150, PlacementDirection: "AlongHost"),
            Array.Empty<string>());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => service.BuildPlacements(bounds, [wallItem]));
        Assert.Contains("плиты", exception.Message, StringComparison.Ordinal);
    }

    private static RebarRulePreviewItem CreateItem(
        string zoneId,
        string placementDirection,
        double spacingMillimeters)
    {
        return new RebarRulePreviewItem(
            zoneId,
            zoneId.ToUpperInvariant(),
            new RebarRule(
                $"Rule {zoneId}",
                "Slab",
                "Ø10 A500",
                spacingMillimeters,
                PlacementDirection: placementDirection),
            Array.Empty<string>());
    }
}
