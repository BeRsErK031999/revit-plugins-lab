using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldSlabRebarLayoutServiceTests
{
    private readonly IsoFieldSlabRebarLayoutService service = new();

    [Fact]
    public void BuildSegments_ClipsBarsAtHole()
    {
        RebarRulePreviewItem item = CreateItem(
            CreateRegion(
                CreateLoop(0, 0, 10, 4),
                [CreateLoop(4, 1, 6, 3)]),
            new IsoFieldRebarComponent(10, 304.8, 0, 1));
        IsoFieldEngineeringSettings settings = new(
            IsoFieldReinforcementMode.FullCombination,
            ConcreteCoverMillimeters: 30,
            BoundaryOffsetMillimeters: 30.48,
            MinimumBarLengthMillimeters: 100);

        IReadOnlyList<IsoFieldSlabRebarSegment> segments = service.BuildSegments([item], settings);

        Assert.Equal(6, segments.Count);
        Assert.Contains(segments, segment => segment.StartFeet.X < 4 && segment.EndFeet.X < 4);
        Assert.Contains(segments, segment => segment.StartFeet.X > 6 && segment.EndFeet.X > 6);
        Assert.All(segments, segment => Assert.DoesNotContain(" ", segment.StableId));
    }

    [Fact]
    public void BuildSegments_UsesComponentPhaseToAvoidCoincidentBars()
    {
        IsoFieldPolygonRegion region = CreateRegion(CreateLoop(0, 0, 10, 4));
        RebarRulePreviewItem item = CreateItem(
            region,
            new IsoFieldRebarComponent(10, 304.8, 0, 2),
            new IsoFieldRebarComponent(10, 304.8, 1, 2));
        IsoFieldEngineeringSettings settings = new(
            IsoFieldReinforcementMode.FullCombination,
            ConcreteCoverMillimeters: 30,
            BoundaryOffsetMillimeters: 0,
            MinimumBarLengthMillimeters: 100);

        IReadOnlyList<IsoFieldSlabRebarSegment> segments = service.BuildSegments([item], settings);

        double[] firstComponentRows = segments
            .Where(segment => segment.Component.CombinationIndex == 0)
            .Select(segment => segment.StartFeet.Y)
            .ToArray();
        double[] secondComponentRows = segments
            .Where(segment => segment.Component.CombinationIndex == 1)
            .Select(segment => segment.StartFeet.Y)
            .ToArray();
        Assert.NotEmpty(firstComponentRows);
        Assert.NotEmpty(secondComponentRows);
        Assert.DoesNotContain(secondComponentRows, row => firstComponentRows.Contains(row));
    }

    [Fact]
    public void BuildSegments_RejectsLayoutOverSafetyLimit()
    {
        RebarRulePreviewItem item = CreateItem(
            CreateRegion(CreateLoop(0, 0, 10, 10)),
            new IsoFieldRebarComponent(10, 50, 0, 1));
        IsoFieldEngineeringSettings settings = new(
            IsoFieldReinforcementMode.FullCombination,
            ConcreteCoverMillimeters: 30,
            BoundaryOffsetMillimeters: 0,
            MinimumBarLengthMillimeters: 100,
            MaximumBarCount: 5);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => service.BuildSegments([item], settings));

        Assert.Contains("больше 5", exception.Message, StringComparison.Ordinal);
    }

    private static RebarRulePreviewItem CreateItem(
        IsoFieldPolygonRegion region,
        params IsoFieldRebarComponent[] components)
    {
        double area = components.Sum(component => component.AreaSquareCentimetersPerMeter);
        return new RebarRulePreviewItem(
            "As1X:zone-a",
            "Zone A",
            new RebarRule(
                "Rule A",
                "Slab",
                components[0].BarTypeName,
                components[0].SpacingMillimeters,
                PlacementDirection: "X",
                RequiredAreaSquareCentimetersPerMeter: area,
                ProvidedAreaSquareCentimetersPerMeter: area,
                ReinforcementLabel: string.Join("+", components.Select(component => component.DisplayName)),
                LayerRole: IsoFieldLayerRole.As1X,
                Face: IsoFieldRebarFace.Bottom,
                Components: components,
                ReinforcementMode: IsoFieldReinforcementMode.FullCombination),
            Array.Empty<string>(),
            [region]);
    }

    private static IsoFieldPolygonRegion CreateRegion(
        IReadOnlyList<IsoFieldPoint> outer,
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>>? holes = null)
    {
        return new IsoFieldPolygonRegion(
            outer,
            holes ?? Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            AreaSquareFeet: 1);
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
}
