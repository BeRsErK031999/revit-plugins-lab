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

    [Fact]
    public void BuildEngineeringPlacements_MapsLocalLineToInteriorWallFace()
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
                "Wall",
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
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            new IsoFieldRebarPoint3D(0, -1, 0),
            Array.Empty<IReadOnlyList<IsoFieldPoint>>());

        IsoFieldRebarPlacement placement = Assert.Single(
            service.BuildEngineeringPlacements(geometry, wallThicknessFeet: 1, preview));

        Assert.Equal(100.1, placement.Start.XFeet, 6);
        Assert.Equal(200 + 1 - (35d / 304.8), placement.Start.YFeet, 6);
        Assert.Equal(10.1, placement.Start.ZFeet, 6);
        Assert.Equal(component, placement.Component);
        Assert.Contains("As1X:zone-a", placement.StableId, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEngineeringPlacements_RejectsWallThatIsTooThinForBothFaces()
    {
        IsoFieldRebarComponent component = new(16, 100, 0, 1);
        RebarRulePreviewResult preview = CreateEngineeringPreview(component);
        IsoFieldHostGeometry geometry = new(
            new IsoFieldRebarPoint3D(0, 0, 0),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            new IsoFieldRebarPoint3D(0, -1, 0),
            Array.Empty<IReadOnlyList<IsoFieldPoint>>());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.BuildEngineeringPlacements(geometry, wallThicknessFeet: 0.2, preview));

        Assert.Contains("Толщины стены недостаточно", exception.Message, StringComparison.Ordinal);
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

    private static RebarRulePreviewResult CreateEngineeringPreview(IsoFieldRebarComponent component)
    {
        IsoFieldPolygonRegion region = new(
            [
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(2, 0),
                new IsoFieldPoint(2, 2),
                new IsoFieldPoint(0, 2),
                new IsoFieldPoint(0, 0)
            ],
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            AreaSquareFeet: 4);
        RebarRulePreviewItem CreateItem(string id, IsoFieldRebarFace face)
        {
            return new RebarRulePreviewItem(
                id,
                id,
                new RebarRule(
                    id,
                    "Wall",
                    component.BarTypeName,
                    component.SpacingMillimeters,
                    PlacementDirection: "X",
                    RequiredAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
                    ProvidedAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
                    ReinforcementLabel: component.DisplayName,
                    LayerRole: face == IsoFieldRebarFace.Bottom
                        ? IsoFieldLayerRole.As1X
                        : IsoFieldLayerRole.As2X,
                    Face: face,
                    Components: [component],
                    ReinforcementMode: IsoFieldReinforcementMode.FullCombination),
                Array.Empty<string>(),
                [region]);
        }

        IsoFieldEngineeringSettings settings = new(
            IsoFieldReinforcementMode.FullCombination,
            ConcreteCoverMillimeters: 30,
            BoundaryOffsetMillimeters: 0,
            MinimumBarLengthMillimeters: 100);
        return new RebarRulePreviewResult(
            [
                CreateItem("interior", IsoFieldRebarFace.Bottom),
                CreateItem("exterior", IsoFieldRebarFace.Top)
            ],
            Array.Empty<string>(),
            settings,
            2);
    }
}
