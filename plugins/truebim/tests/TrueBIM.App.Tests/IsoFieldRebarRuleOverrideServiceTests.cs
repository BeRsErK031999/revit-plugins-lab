using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRebarRuleOverrideServiceTests
{
    private readonly IsoFieldRebarRuleOverrideService service = new();

    [Fact]
    public void Validate_RejectsCombinationBelowRequiredArea()
    {
        RebarRulePreviewItem item = CreateItem("zone-a", "X", IsoFieldLayerRole.As1X);

        IsoFieldRebarRuleOverrideValidation result = service.Validate(
            item,
            CreateSettings(),
            isIncluded: true,
            "d10s200");

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("меньше требуемой", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_ReplacesRuleAndRecalculatesBarCount()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", "X", IsoFieldLayerRole.As1X));

        RebarRulePreviewResult result = service.Apply(
            preview,
            new Dictionary<string, IsoFieldRebarRuleOverride>
            {
                ["zone-a"] = new("zone-a", true, "d16s200")
            });

        RebarRulePreviewItem item = Assert.Single(result.Items);
        Assert.True(result.CanCreateRebar);
        Assert.True(item.IsIncluded);
        Assert.True(item.IsManuallyOverridden);
        Assert.Equal("d16s200", item.Rule.ReinforcementLabel);
        Assert.Equal(16, Assert.Single(item.Rule.EffectiveComponents).DiameterMillimeters);
        Assert.True(item.EstimatedBarCount > 0);
        Assert.Equal(item.EstimatedBarCount, result.EstimatedBarCount);
    }

    [Fact]
    public void Apply_ExcludedZoneRemainsVisibleButLeavesActiveLayout()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", "X", IsoFieldLayerRole.As1X),
            CreateItem("zone-b", "Y", IsoFieldLayerRole.As3Y));

        RebarRulePreviewResult result = service.Apply(
            preview,
            new Dictionary<string, IsoFieldRebarRuleOverride>
            {
                ["zone-a"] = new("zone-a", false, "d12s200")
            });

        Assert.True(result.CanCreateRebar);
        Assert.Single(result.ActiveItems);
        RebarRulePreviewItem excluded = Assert.Single(result.Items, item => item.ZoneId == "zone-a");
        Assert.False(excluded.IsIncluded);
        Assert.True(excluded.IsManuallyOverridden);
        Assert.Equal(0, excluded.EstimatedBarCount);
    }

    [Fact]
    public void Apply_BlocksLayoutWhenEveryZoneIsExcluded()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", "X", IsoFieldLayerRole.As1X));

        RebarRulePreviewResult result = service.Apply(
            preview,
            new Dictionary<string, IsoFieldRebarRuleOverride>
            {
                ["zone-a"] = new("zone-a", false, "d12s200")
            });

        Assert.False(result.CanCreateRebar);
        Assert.Empty(result.ActiveItems);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("Все зоны исключены", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_InvalidManualLabelKeepsZoneEditableAndBlocksCreation()
    {
        RebarRulePreviewResult preview = CreatePreview(
            CreateItem("zone-a", "X", IsoFieldLayerRole.As1X));

        RebarRulePreviewResult result = service.Apply(
            preview,
            new Dictionary<string, IsoFieldRebarRuleOverride>
            {
                ["zone-a"] = new("zone-a", true, "wrong")
            });

        RebarRulePreviewItem item = Assert.Single(result.Items);
        Assert.False(result.CanCreateRebar);
        Assert.True(item.IsIncluded);
        Assert.True(item.IsManuallyOverridden);
        Assert.Contains(item.Diagnostics, diagnostic =>
            diagnostic.Contains("формат", StringComparison.Ordinal));
    }

    private static RebarRulePreviewResult CreatePreview(params RebarRulePreviewItem[] items)
    {
        IsoFieldEngineeringSettings settings = CreateSettings();
        IsoFieldSlabRebarLayoutService layoutService = new();
        int barCount = layoutService.BuildSegments(items, settings).Count;
        return new RebarRulePreviewResult(
            items,
            Array.Empty<string>(),
            settings,
            barCount,
            Array.Empty<string>());
    }

    private static RebarRulePreviewItem CreateItem(
        string zoneId,
        string direction,
        IsoFieldLayerRole layerRole)
    {
        IsoFieldRebarComponent component = new(12, 200, 0, 1);
        RebarRule rule = new(
            "Rule " + zoneId,
            "Slab",
            component.BarTypeName,
            component.SpacingMillimeters,
            PlacementDirection: direction,
            RequiredAreaSquareCentimetersPerMeter: 5,
            ProvidedAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
            ReinforcementLabel: component.DisplayName,
            LayerRole: layerRole,
            Face: IsoFieldRebarFace.Bottom,
            Components: [component],
            ReinforcementMode: IsoFieldReinforcementMode.FullCombination);
        IsoFieldPolygonRegion region = new(
            [
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(4, 0),
                new IsoFieldPoint(4, 4),
                new IsoFieldPoint(0, 4),
                new IsoFieldPoint(0, 0)
            ],
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            16);
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
