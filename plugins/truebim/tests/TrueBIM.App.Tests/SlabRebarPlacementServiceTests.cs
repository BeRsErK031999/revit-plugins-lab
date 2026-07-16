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

    [Fact]
    public void BuildEngineeringPlacements_MapsLocalClippedLineToBottomFace()
    {
        IsoFieldRebarComponent component = new(10, 304.8, 0, 1);
        IsoFieldPolygonRegion region = new(
            [
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(2, 0),
                new IsoFieldPoint(2, 1),
                new IsoFieldPoint(0, 1),
                new IsoFieldPoint(0, 0)
            ],
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            AreaSquareFeet: 2);
        RebarRulePreviewItem item = new(
            "As1X:zone-a",
            "Zone A",
            new RebarRule(
                "Rule A",
                "Slab",
                component.BarTypeName,
                component.SpacingMillimeters,
                PlacementDirection: "X",
                RequiredAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
                ProvidedAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
                ReinforcementLabel: component.DisplayName,
                LayerRole: IsoFieldLayerRole.As1X,
                Face: IsoFieldRebarFace.Bottom,
                Components: [component],
                ReinforcementMode: IsoFieldReinforcementMode.FullCombination),
            Array.Empty<string>(),
            [region]);
        IsoFieldEngineeringSettings settings = new(
            IsoFieldReinforcementMode.FullCombination,
            ConcreteCoverMillimeters: 30,
            BoundaryOffsetMillimeters: 30.48,
            MinimumBarLengthMillimeters: 100);
        RebarRulePreviewResult preview = new([item], Array.Empty<string>(), settings, 1);
        IsoFieldHostGeometry geometry = new(
            new IsoFieldRebarPoint3D(100, 200, 10),
            new IsoFieldRebarPoint3D(0, 1, 0),
            new IsoFieldRebarPoint3D(-1, 0, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            Array.Empty<IReadOnlyList<IsoFieldPoint>>());

        IsoFieldRebarPlacement placement = Assert.Single(
            service.BuildEngineeringPlacements(geometry, slabThicknessFeet: 1, preview));

        Assert.Equal(99.9, placement.Start.XFeet, 6);
        Assert.Equal(200.1, placement.Start.YFeet, 6);
        Assert.Equal(10 - 1 + (35d / 304.8), placement.Start.ZFeet, 6);
        Assert.Equal(component, placement.Component);
        Assert.Contains("As1X:zone-a", placement.StableId, StringComparison.Ordinal);
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
