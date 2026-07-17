using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRebarZoneMergeServiceTests
{
    private readonly IsoFieldRebarZoneMergeService service = new();

    [Fact]
    public void CreateMerge_UsesStableIdIndependentOfSelectionOrder()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", 0, 0, 4, 4),
            CreateItem("zone-b", 4, 0, 8, 4));

        IsoFieldRebarZoneMerge forward = service.CreateMerge(preview, ["zone-a", "zone-b"]);
        IsoFieldRebarZoneMerge reverse = service.CreateMerge(preview, ["zone-b", "zone-a"]);

        Assert.Equal(forward.MergedZoneId, reverse.MergedZoneId);
        Assert.Equal(["zone-a", "zone-b"], forward.SourceZoneIds);
        Assert.StartsWith("zone-merge-", forward.MergedZoneId, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UnionsAdjacentZonesAndRecalculatesContinuousLayout()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", 0, 0, 4, 4),
            CreateItem("zone-b", 4, 0, 8, 4));
        IsoFieldRebarZoneMerge merge = service.CreateMerge(preview, ["zone-b", "zone-a"]);

        RebarRulePreviewResult result = service.Apply(preview, [merge]);

        RebarRulePreviewItem item = Assert.Single(result.Items);
        Assert.True(result.CanCreateRebar);
        Assert.True(item.IsMerged);
        Assert.Equal(merge.MergedZoneId, item.ZoneId);
        Assert.Equal(["zone-a", "zone-b"], item.EffectiveSourceZoneIds);
        Assert.Single(item.EffectiveRegions);
        Assert.Equal(32, item.EffectiveRegions[0].AreaSquareFeet, 6);
        Assert.True(item.EstimatedBarCount > 0);
        Assert.Equal(item.EstimatedBarCount, result.EstimatedBarCount);
    }

    [Fact]
    public void Apply_UsesMaximumRequiredAreaAcrossMergedSources()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", 0, 0, 4, 4, requiredArea: 4.5),
            CreateItem("zone-b", 4, 0, 8, 4, requiredArea: 5.5));
        IsoFieldRebarZoneMerge merge = service.CreateMerge(preview, ["zone-a", "zone-b"]);

        RebarRulePreviewItem item = Assert.Single(service.Apply(preview, [merge]).Items);

        Assert.Equal(5.5, item.Rule.RequiredAreaSquareCentimetersPerMeter);
        Assert.True(item.Rule.ProvidedAreaSquareCentimetersPerMeter >= 5.5);
    }

    [Fact]
    public void CreateMerge_RejectsDisconnectedZones()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", 0, 0, 4, 4),
            CreateItem("zone-b", 5, 0, 9, 4));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateMerge(preview, ["zone-a", "zone-b"]));

        Assert.Contains("не образуют один непрерывный регион", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMerge_RejectsDifferentEngineeringRules()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", 0, 0, 4, 4),
            CreateItem("zone-b", 4, 0, 8, 4, diameterMillimeters: 16));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateMerge(preview, ["zone-a", "zone-b"]));

        Assert.Contains("одинаковые слой, грань, направление и сочетание", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RejectsSourceUsedByMultipleMergeGroups()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", 0, 0, 4, 4),
            CreateItem("zone-b", 4, 0, 8, 4),
            CreateItem("zone-c", 8, 0, 12, 4));
        IsoFieldRebarZoneMerge first = service.CreateMerge(preview, ["zone-a", "zone-b"]);
        IsoFieldRebarZoneMerge second = service.CreateMerge(preview, ["zone-b", "zone-c"]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.Apply(preview, [first, second]));

        Assert.Contains("более чем в одно", exception.Message, StringComparison.Ordinal);
    }

    private static RebarRulePreviewResult CreatePreview(params RebarRulePreviewItem[] items)
    {
        IsoFieldEngineeringSettings settings = CreateSettings();
        int barCount = new IsoFieldSlabRebarLayoutService().BuildSegments(items, settings).Count;
        return new RebarRulePreviewResult(
            items,
            Array.Empty<string>(),
            settings,
            barCount,
            Array.Empty<string>());
    }

    private static RebarRulePreviewItem CreateItem(
        string zoneId,
        double minX,
        double minY,
        double maxX,
        double maxY,
        double diameterMillimeters = 12,
        double requiredArea = 5)
    {
        IsoFieldRebarComponent component = new(diameterMillimeters, 200, 0, 1);
        RebarRule rule = new(
            "Rule " + zoneId,
            "Slab",
            component.BarTypeName,
            component.SpacingMillimeters,
            PlacementDirection: "X",
            RequiredAreaSquareCentimetersPerMeter: requiredArea,
            ProvidedAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
            ReinforcementLabel: component.DisplayName,
            LayerRole: IsoFieldLayerRole.As1X,
            Face: IsoFieldRebarFace.Bottom,
            Components: [component],
            ReinforcementMode: IsoFieldReinforcementMode.FullCombination);
        IsoFieldPolygonRegion region = new(
            [
                new IsoFieldPoint(minX, minY),
                new IsoFieldPoint(maxX, minY),
                new IsoFieldPoint(maxX, maxY),
                new IsoFieldPoint(minX, maxY),
                new IsoFieldPoint(minX, minY)
            ],
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            (maxX - minX) * (maxY - minY));
        return new RebarRulePreviewItem(
            zoneId,
            "Zone " + zoneId,
            rule,
            Array.Empty<string>(),
            [region],
            BaseDiagnostics: Array.Empty<string>());
    }

    private static IsoFieldEngineeringSettings CreateSettings()
    {
        return new IsoFieldEngineeringSettings(
            IsoFieldReinforcementMode.FullCombination,
            ConcreteCoverMillimeters: 30,
            BoundaryOffsetMillimeters: 0,
            MinimumBarLengthMillimeters: 100);
    }
}
