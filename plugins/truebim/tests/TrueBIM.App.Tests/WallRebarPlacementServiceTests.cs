using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Revit;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class WallRebarPlacementServiceTests
{
    private readonly WallRebarPlacementService service = new();

    [Fact]
    public void BuildPlacements_UsesWallAxisForAlongHostDirection()
    {
        IsoFieldWallPlacementFrame frame = CreateFrame(heightFeet: 4);
        RebarRulePreviewItem item = CreateWallItem("zone-a", "AlongHost", spacingMillimeters: 150);

        IsoFieldRebarPlacement placement = Assert.Single(service.BuildPlacements(frame, [item]));

        Assert.True(placement.End.XFeet > placement.Start.XFeet);
        Assert.Equal(0, placement.Start.YFeet, precision: 6);
        Assert.Equal(0, placement.End.YFeet, precision: 6);
        Assert.Equal(2, placement.Start.ZFeet, precision: 6);
        Assert.Equal(2, placement.End.ZFeet, precision: 6);
        Assert.Equal(-1, placement.Normal.YFeet, precision: 6);
    }

    [Fact]
    public void BuildPlacements_CreatesCenteredHeightOffsetsForMultipleWallZones()
    {
        IsoFieldWallPlacementFrame frame = CreateFrame(centerZFeet: 3, heightFeet: 6);

        IReadOnlyList<IsoFieldRebarPlacement> placements = service.BuildPlacements(
            frame,
            [
                CreateWallItem("zone-a", "AlongHost", spacingMillimeters: 150),
                CreateWallItem("zone-b", "AlongHost", spacingMillimeters: 150),
                CreateWallItem("zone-c", "AlongHost", spacingMillimeters: 150)
            ]);

        Assert.Equal(3, placements.Count);
        Assert.True(placements[0].Start.ZFeet < placements[1].Start.ZFeet);
        Assert.True(placements[1].Start.ZFeet < placements[2].Start.ZFeet);
        Assert.Equal(3, placements[1].Start.ZFeet, precision: 6);
        Assert.All(placements, placement => Assert.True(placement.LengthFeet >= 0.5));
    }

    [Fact]
    public void BuildPlacements_UsesVerticalDirectionWhenRequested()
    {
        IsoFieldWallPlacementFrame frame = CreateFrame(centerZFeet: 3, heightFeet: 6);
        RebarRulePreviewItem item = CreateWallItem("zone-a", "Vertical", spacingMillimeters: 150);

        IsoFieldRebarPlacement placement = Assert.Single(service.BuildPlacements(frame, [item]));

        Assert.True(placement.End.ZFeet > placement.Start.ZFeet);
        Assert.Equal(5, placement.Start.XFeet, precision: 6);
        Assert.Equal(5, placement.End.XFeet, precision: 6);
        Assert.Equal(0, placement.Start.YFeet, precision: 6);
        Assert.Equal(0, placement.End.YFeet, precision: 6);
        Assert.Equal(-1, placement.Normal.YFeet, precision: 6);
    }

    [Fact]
    public void BuildPlacements_RejectsNonWallRules()
    {
        IsoFieldWallPlacementFrame frame = CreateFrame(heightFeet: 4);
        RebarRulePreviewItem slabItem = new(
            "zone-a",
            "Zone A",
            new RebarRule("Rule A", "Slab", "D10 A500", 150, PlacementDirection: "Auto"),
            Array.Empty<string>());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => service.BuildPlacements(frame, [slabItem]));
        Assert.Contains("стены", exception.Message, StringComparison.Ordinal);
    }

    private static IsoFieldWallPlacementFrame CreateFrame(
        double centerZFeet = 2,
        double heightFeet = 4)
    {
        return new IsoFieldWallPlacementFrame(
            new IsoFieldRebarPoint3D(5, 0, centerZFeet),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, -1, 0),
            LengthFeet: 10,
            HeightFeet: heightFeet);
    }

    private static RebarRulePreviewItem CreateWallItem(
        string zoneId,
        string placementDirection,
        double spacingMillimeters)
    {
        return new RebarRulePreviewItem(
            zoneId,
            zoneId.ToUpperInvariant(),
            new RebarRule(
                $"Rule {zoneId}",
                "Wall",
                "D12 A500",
                spacingMillimeters,
                PlacementDirection: placementDirection),
            Array.Empty<string>());
    }
}
