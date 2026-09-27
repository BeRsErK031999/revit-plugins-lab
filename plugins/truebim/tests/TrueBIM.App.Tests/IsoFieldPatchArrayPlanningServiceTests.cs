using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldPatchArrayPlanningServiceTests
{
    private const double MillimetersPerFoot = 304.8;
    private readonly IsoFieldPatchArrayPlanningService service = new();

    [Fact]
    public void Build_NestedGuideLevelsCreateOneArrayWithMaximumReinforcementAndFullWidth()
    {
        RebarRulePreviewResult preview = Preview(
            Item("outer", -757, -1300, 757, 1300, 10, 400),
            Item("middle", -471, -700, 471, 700, 10, 200),
            Item("maximum", -126, -250, 126, 250, 12, 200));

        RebarRulePreviewResult result = service.Build(preview, Host(), new Dictionary<double, double> { [12] = 500 });

        RebarRulePreviewItem patch = Assert.Single(result.Items);
        Assert.Empty(patch.Diagnostics);
        Assert.True(result.CanCreateRebar);
        Assert.True(patch.IsArrayEnvelope);
        Assert.Equal(["maximum", "middle", "outer"], patch.EffectiveSourceZoneIds);
        IsoFieldRebarComponent component = Assert.Single(patch.Rule.EffectiveComponents);
        Assert.Equal(12, component.DiameterMillimeters);
        Assert.Equal(200, component.SpacingMillimeters);
        Assert.Equal(14, patch.EstimatedBarCount);
        AssertBounds(patch, -1057, -1300, 1057, 1300);
        // 2114 мм — геометрический минимум. 2340 из PDF требует отсутствующей таблицы раскроя.
        Assert.Equal(10 + component.AreaSquareCentimetersPerMeter, patch.Rule.ProvidedAreaSquareCentimetersPerMeter!.Value, 6);
    }

    [Fact]
    public void Build_UsesAreaOfAdditionalSteelForEachAnchorageRatio()
    {
        RebarRulePreviewItem high = Item("high", 0, 0, 200, 400, 12, 200);
        RebarRulePreviewItem low = Item("low", 200, 0, 1200, 400, 10, 200);

        RebarRulePreviewItem patch = Assert.Single(service.Build(Preview(high, low), Host(),
            new Dictionary<double, double> { [12] = 500 }).Items);

        Assert.Empty(patch.Diagnostics);
        double extension = 500 * 100.0 / 144;
        AssertBounds(patch, -500, 0, 1200 + extension, 400);
    }

    [Fact]
    public void Build_EnforcesMinimum300BeyondLowIntensityOuterLevel()
    {
        RebarRulePreviewItem high = Item("high", 0, 0, 200, 400, 12, 200);
        RebarRulePreviewItem low = Item("low", 200, 0, 1200, 400, 10, 400);

        RebarRulePreviewItem patch = Assert.Single(service.Build(Preview(high, low), Host(),
            new Dictionary<double, double> { [12] = 500 }).Items);

        AssertBounds(patch, -500, 0, 1500, 400);
    }

    [Fact]
    public void Build_RoundsWidthUpAndOrientsAnchorageAlongY()
    {
        RebarRulePreviewItem item = Item("y", 0, 0, 250, 500, 12, 200);
        item = item with { Rule = item.Rule with { PlacementDirection = "Y", LayerRole = IsoFieldLayerRole.As3Y } };

        RebarRulePreviewItem patch = Assert.Single(service.Build(Preview(item), Host(),
            new Dictionary<double, double> { [12] = 500 }).Items);

        Assert.Empty(patch.Diagnostics);
        Assert.Equal(3, patch.EstimatedBarCount);
        AssertBounds(patch, -75, -500, 325, 1000);
    }

    [Fact]
    public void Build_MissingAnchorageBlocksOnlyAffectedPatchAndKeepsSourceGeometry()
    {
        RebarRulePreviewItem missing = Item("missing", 0, 0, 400, 400, 16, 200);
        RebarRulePreviewItem valid = Item("valid", 3000, 0, 3400, 400, 12, 200);

        RebarRulePreviewResult result = service.Build(Preview(missing, valid), Host(),
            new Dictionary<double, double> { [12] = 500 });

        Assert.Empty(result.Diagnostics);
        RebarRulePreviewItem rejected = Assert.Single(result.Items, item => !item.HasValidRule);
        Assert.Contains(rejected.Diagnostics, value => value.Contains("Ø16", StringComparison.Ordinal));
        Assert.True(rejected.IsIncluded);
        Assert.False(rejected.IsArrayEnvelope);
        Assert.Equal(0, rejected.EstimatedBarCount);
        AssertBounds(rejected, 0, 0, 400, 400);
        Assert.Single(result.Items, item => item.HasValidRule);
    }

    [Fact]
    public void Build_RectangleSpanningHostOpeningIsBlockedEvenWhenSourceDoesNotTouchOpening()
    {
        RebarRulePreviewItem item = Item("opening", 0, 0, 400, 400, 12, 200);
        IsoFieldHostGeometry host = Host(Loop(600, 100, 700, 300));

        RebarRulePreviewItem patch = Assert.Single(service.Build(Preview(item), host,
            new Dictionary<double, double> { [12] = 500 }).Items);

        Assert.False(patch.HasValidRule);
        Assert.Contains(patch.Diagnostics, value => value.Contains("отверстие", StringComparison.Ordinal));
        AssertBounds(patch, 0, 0, 400, 400);
    }

    [Fact]
    public void Build_AnchorageOutsideSlabRequiresBendInsteadOfSilentClipping()
    {
        RebarRulePreviewItem item = Item("edge", 100, 100, 600, 500, 12, 200);
        IsoFieldHostGeometry host = Host() with { BoundaryLoopsFeet = [Loop(0, 0, 5000, 5000)] };

        RebarRulePreviewItem patch = Assert.Single(service.Build(Preview(item), host,
            new Dictionary<double, double> { [12] = 500 }).Items);

        Assert.False(patch.HasValidRule);
        Assert.Contains(patch.Diagnostics, value => value.Contains("загиб", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_HostClearanceAppliesSeparatelyFromAnchorage()
    {
        RebarRulePreviewItem item = Item("clearance", 1000, 10, 1500, 410, 12, 200);
        IsoFieldHostGeometry host = Host() with { BoundaryLoopsFeet = [Loop(0, 0, 5000, 5000)] };

        RebarRulePreviewItem patch = Assert.Single(service.Build(Preview(item), host,
            new Dictionary<double, double> { [12] = 500 }).Items);

        Assert.False(patch.HasValidRule);
        Assert.Contains(patch.Diagnostics, value => value.Contains("контур", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_SeparateRegionsOfOneSourceRemainSeparatePatchesWithUniqueIds()
    {
        RebarRulePreviewItem source = Item("split", 0, 0, 400, 400, 12, 200);
        source = source with { Regions = [Region(0, 0, 400, 400), Region(3000, 0, 3400, 400)] };

        RebarRulePreviewResult result = service.Build(Preview(source), Host(), new Dictionary<double, double> { [12] = 500 });

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Items.Select(item => item.ZoneId).Distinct().Count());
        Assert.All(result.Items, item => Assert.Equal(["split"], item.EffectiveSourceZoneIds));
        Assert.All(result.Items, item => Assert.True(item.HasValidRule));
    }

    [Fact]
    public void Build_KeepsMapsAndFacesSeparateAndIdsStableAcrossInputOrder()
    {
        RebarRulePreviewItem bottom = Item("bottom", 0, 0, 400, 400, 12, 200);
        RebarRulePreviewItem top = Item("top", 0, 0, 400, 400, 12, 200);
        top = top with { Rule = top.Rule with { Face = IsoFieldRebarFace.Top, LayerRole = IsoFieldLayerRole.As2X } };
        Dictionary<double, double> anchorage = new() { [12] = 500 };

        RebarRulePreviewResult forward = service.Build(Preview(bottom, top), Host(), anchorage);
        RebarRulePreviewResult reverse = service.Build(Preview(top, bottom), Host(), anchorage);

        Assert.Equal(2, forward.Items.Count);
        Assert.Equal(forward.Items.Select(item => item.ZoneId), reverse.Items.Select(item => item.ZoneId));
    }

    [Fact]
    public void Build_RejectsInsufficientSingleComponentInsteadOfLosingSecondAdditionalComponent()
    {
        RebarRulePreviewItem item = Item("combination", 0, 0, 400, 400, 12, 200);
        IsoFieldRebarComponent first = item.Rule.EffectiveComponents[0];
        item = item with
        {
            Rule = item.Rule with
            {
                Components = [first, first with { CombinationIndex = 2, CombinationCount = 3 }],
                ProvidedAreaSquareCentimetersPerMeter = 10 + (first.AreaSquareCentimetersPerMeter * 2),
                RequiredAreaSquareCentimetersPerMeter = 10 + (first.AreaSquareCentimetersPerMeter * 2)
            }
        };

        RebarRulePreviewItem patch = Assert.Single(service.Build(Preview(item), Host(), new Dictionary<double, double> { [12] = 500 }).Items);

        Assert.False(patch.HasValidRule);
        Assert.Contains(patch.Diagnostics, value => value.Contains("не обеспечивает", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(14, true)]
    [InlineData(14.01, false)]
    public void Build_UsesSameLegendRoundingToleranceAsRawRuleValidation(double requiredArea, bool accepted)
    {
        RebarRulePreviewItem item = Item("d10s200+d16s200", 0, 0, 400, 400, 16, 200);
        double providedArea = new IsoFieldRebarComponent(10, 200, 0, 2).AreaSquareCentimetersPerMeter
            + item.Rule.EffectiveComponents[0].AreaSquareCentimetersPerMeter;
        item = item with
        {
            Rule = item.Rule with
            {
                RequiredAreaSquareCentimetersPerMeter = requiredArea,
                ProvidedAreaSquareCentimetersPerMeter = providedArea
            }
        };
        Assert.Equal(13.98008730847458, providedArea, 10);
        Assert.Equal(accepted, new RebarRuleValidationService().ValidateRule(item.Rule).Count == 0);

        RebarRulePreviewItem patch = Assert.Single(service.Build(Preview(item), Host(),
            new Dictionary<double, double> { [16] = 665 }).Items);

        Assert.Equal(accepted, patch.HasValidRule);
        Assert.Equal(accepted, patch.IsArrayEnvelope);
        Assert.Equal(providedArea, patch.Rule.ProvidedAreaSquareCentimetersPerMeter!.Value, 10);
        Assert.Equal("d16s200", Assert.Single(patch.Rule.EffectiveComponents).DisplayName);
        if (!accepted)
        {
            Assert.Contains(patch.Diagnostics, value => value.Contains("не обеспечивает", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Build_NormalizesGuideArrayToInspectedFamilyLength()
    {
        RebarRulePreviewResult preview = Preview(
            Item("outer", -757, -1300, 757, 1300, 10, 400),
            Item("middle", -471, -700, 471, 700, 10, 200),
            Item("maximum", -126, -250, 126, 250, 12, 200));

        RebarRulePreviewItem patch = Assert.Single(service.Build(preview, Host(),
            new Dictionary<double, double> { [12] = 500 },
            new IsoFieldArrayFabricationLengthService().NormalizeMinimumLengthMillimeters).Items);

        Assert.Empty(patch.Diagnostics);
        Assert.True(patch.IsArrayEnvelope);
        Assert.Equal(14, patch.EstimatedBarCount);
        AssertBounds(patch, -1170, -1300, 1170, 1300);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_ChecksHostBoundaryAndOpeningAfterFabricationLengthIncreases(bool opening)
    {
        RebarRulePreviewResult preview = Preview(Item("fabrication", 1000, 1000, 1500, 1400, 12, 200));
        IsoFieldHostGeometry host = opening
            ? Host(Loop(2040, 1100, 2070, 1300))
            : Host() with { BoundaryLoopsFeet = [Loop(450, 0, 5000, 5000)] };
        Dictionary<double, double> anchorage = new() { [12] = 500 };
        RebarRulePreviewItem geometric = Assert.Single(service.Build(preview, host, anchorage).Items);
        Assert.Empty(geometric.Diagnostics);
        AssertBounds(geometric, 500, 1000, 2000, 1400);

        RebarRulePreviewItem fabricated = Assert.Single(service.Build(preview, host, anchorage,
            new IsoFieldArrayFabricationLengthService().NormalizeMinimumLengthMillimeters).Items);

        Assert.False(fabricated.HasValidRule);
        Assert.False(fabricated.IsArrayEnvelope);
        Assert.Contains(fabricated.Diagnostics, value => value.Contains("контур плиты", StringComparison.Ordinal));
        AssertBounds(fabricated, 1000, 1000, 1500, 1400);
    }

    [Fact]
    public void Build_OverlongFabricationBlocksPatchWithoutReturningItsArrayEnvelope()
    {
        RebarRulePreviewResult preview = Preview(Item("overlong", -5400, 0, 5400, 400, 12, 200));

        RebarRulePreviewItem patch = Assert.Single(service.Build(preview, Host(),
            new Dictionary<double, double> { [12] = 500 },
            new IsoFieldArrayFabricationLengthService().NormalizeMinimumLengthMillimeters).Items);

        Assert.False(patch.HasValidRule);
        Assert.False(patch.IsArrayEnvelope);
        Assert.Equal(0, patch.EstimatedBarCount);
        Assert.Contains(patch.Diagnostics, value => value.Contains("11700", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_NormalizerCannotShortenRequiredAnchorage()
    {
        RebarRulePreviewResult preview = Preview(Item("shortened", 0, 0, 400, 400, 12, 200));

        RebarRulePreviewItem patch = Assert.Single(service.Build(preview, Host(),
            new Dictionary<double, double> { [12] = 500 }, minimum => minimum - 100).Items);

        Assert.False(patch.HasValidRule);
        Assert.False(patch.IsArrayEnvelope);
        Assert.Contains(patch.Diagnostics, value => value.Contains("уменьшил", StringComparison.Ordinal));
    }

    private static RebarRulePreviewResult Preview(params RebarRulePreviewItem[] items) =>
        new(items, Array.Empty<string>(), IsoFieldEngineeringSettings.Default);

    private static RebarRulePreviewItem Item(string id, double minX, double minY, double maxX, double maxY, double diameter, double spacing)
    {
        IsoFieldRebarComponent component = new(diameter, spacing, 1, 2);
        RebarRule rule = new("Rule " + id, "Slab", component.BarTypeName, spacing,
            PlacementDirection: "X",
            RequiredAreaSquareCentimetersPerMeter: 10 + component.AreaSquareCentimetersPerMeter,
            ProvidedAreaSquareCentimetersPerMeter: 10 + component.AreaSquareCentimetersPerMeter,
            LayerRole: IsoFieldLayerRole.As1X,
            Face: IsoFieldRebarFace.Bottom,
            Components: [component],
            ReinforcementMode: IsoFieldReinforcementMode.AdditionalOverBase);
        return new RebarRulePreviewItem(id, id, rule, Array.Empty<string>(), [Region(minX, minY, maxX, maxY)]);
    }

    private static IsoFieldHostGeometry Host(params IReadOnlyList<IsoFieldPoint>[] holes) =>
        new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), [Loop(-10000, -10000, 10000, 10000), .. holes]);

    private static IsoFieldPolygonRegion Region(double minX, double minY, double maxX, double maxY) =>
        new(Loop(minX, minY, maxX, maxY), Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            (maxX - minX) * (maxY - minY) / (MillimetersPerFoot * MillimetersPerFoot));

    private static IReadOnlyList<IsoFieldPoint> Loop(double minX, double minY, double maxX, double maxY) =>
    [
        new(minX / MillimetersPerFoot, minY / MillimetersPerFoot),
        new(maxX / MillimetersPerFoot, minY / MillimetersPerFoot),
        new(maxX / MillimetersPerFoot, maxY / MillimetersPerFoot),
        new(minX / MillimetersPerFoot, maxY / MillimetersPerFoot),
        new(minX / MillimetersPerFoot, minY / MillimetersPerFoot)
    ];

    private static void AssertBounds(RebarRulePreviewItem item, double minX, double minY, double maxX, double maxY)
    {
        IReadOnlyList<IsoFieldPoint> points = Assert.Single(item.EffectiveRegions).OuterBoundaryFeet;
        Assert.Equal(minX, points.Min(point => point.X) * MillimetersPerFoot, 5);
        Assert.Equal(minY, points.Min(point => point.Y) * MillimetersPerFoot, 5);
        Assert.Equal(maxX, points.Max(point => point.X) * MillimetersPerFoot, 5);
        Assert.Equal(maxY, points.Max(point => point.Y) * MillimetersPerFoot, 5);
    }
}
