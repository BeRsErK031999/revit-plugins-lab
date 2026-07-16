using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRebarReviewServiceTests
{
    private readonly IsoFieldRebarReviewService service = new();

    [Fact]
    public void BuildRows_DescribesCalculatedZoneBeforeComparison()
    {
        IReadOnlyList<IsoFieldRebarReviewRow> rows = service.BuildRows(
            CreatePreview(),
            CreateRecognition(confidence: 0.91));

        IsoFieldRebarReviewRow row = Assert.Single(rows);
        Assert.Equal(IsoFieldRebarReviewStatus.NotCompared, row.Status);
        Assert.Equal("As1X", row.LayerText);
        Assert.Equal("91%", row.ConfidenceText);
        Assert.Equal("12", row.EstimatedBarCountText);
        Assert.Contains("Ø12/200", row.ReinforcementText, StringComparison.Ordinal);
        Assert.Contains("8 → 9", row.AreaText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRows_AggregatesMixedChangesForZone()
    {
        IsoFieldRebarChangePlan plan = new(
            [
                CreateChange(IsoFieldRebarChangeKind.Add, "As1X:zone-a:c0:r0:b0"),
                CreateChange(IsoFieldRebarChangeKind.Update, "As1X:zone-a:c0:r1:b0", [20]),
                CreateChange(IsoFieldRebarChangeKind.Delete, "As1X:zone-a:c0:r2:b0", [21, 22]),
                CreateChange(IsoFieldRebarChangeKind.Unchanged, "As1X:zone-a:c0:r3:b0", [23])
            ],
            Array.Empty<string>());

        IsoFieldRebarReviewRow row = Assert.Single(service.BuildRows(
            CreatePreview(),
            CreateRecognition(confidence: 0.8),
            plan));

        Assert.Equal(IsoFieldRebarReviewStatus.Mixed, row.Status);
        Assert.Equal(1, row.AddCount);
        Assert.Equal(1, row.UpdateCount);
        Assert.Equal(2, row.DeleteCount);
        Assert.Equal(1, row.UnchangedCount);
        Assert.Equal("+1 · ~1 · −2 · =1", row.ChangeSummary);
    }

    [Fact]
    public void BuildRows_AppendsObsoleteOwnedZoneScheduledForDeletion()
    {
        IsoFieldRebarChangePlan plan = new(
            [CreateChange(IsoFieldRebarChangeKind.Delete, "As2X:old-zone:c0:r0:b0", [44, 45])],
            Array.Empty<string>());

        IReadOnlyList<IsoFieldRebarReviewRow> rows = service.BuildRows(
            CreatePreview(),
            CreateRecognition(confidence: 0.8),
            plan);

        IsoFieldRebarReviewRow obsolete = Assert.Single(rows, row => row.ZoneId == "old-zone");
        Assert.Equal(IsoFieldLayerRole.As2X, obsolete.LayerRole);
        Assert.Equal(IsoFieldRebarReviewStatus.Delete, obsolete.Status);
        Assert.Equal(2, obsolete.DeleteCount);
    }

    [Theory]
    [InlineData("zone", null, null, null, null, null, true)]
    [InlineData("missing", null, null, null, null, null, false)]
    [InlineData("", IsoFieldLayerRole.As1X, IsoFieldRebarReviewStatus.Update, 12d, 200d, 0.8, true)]
    [InlineData("", IsoFieldLayerRole.As2X, null, null, null, null, false)]
    [InlineData("", null, null, 16d, null, null, false)]
    [InlineData("", null, null, null, null, 0.95, false)]
    public void MatchesFilter_AppliesAllReviewCriteria(
        string search,
        IsoFieldLayerRole? layer,
        IsoFieldRebarReviewStatus? status,
        double? diameter,
        double? spacing,
        double? confidence,
        bool expected)
    {
        IsoFieldRebarReviewRow row = new(
            "zone-a",
            "Zone A",
            IsoFieldLayerRole.As1X,
            IsoFieldRebarReviewStatus.Update,
            "X · низ",
            "Ø12/200",
            "8 → 9 см²/м",
            12,
            0.9,
            [12],
            [200],
            0,
            1,
            0,
            0,
            Array.Empty<string>());

        Assert.Equal(expected, service.MatchesFilter(
            row,
            new IsoFieldRebarReviewFilter(
                search,
                layer,
                status,
                diameter,
                spacing,
                confidence)));
    }

    private static RebarRulePreviewResult CreatePreview()
    {
        IsoFieldRebarComponent component = new(12, 200, 0, 1);
        RebarRule rule = new(
            "As1X zone",
            "Slab",
            component.BarTypeName,
            component.SpacingMillimeters,
            PlacementDirection: "X",
            RequiredAreaSquareCentimetersPerMeter: 8,
            ProvidedAreaSquareCentimetersPerMeter: 9,
            ReinforcementLabel: component.DisplayName,
            LayerRole: IsoFieldLayerRole.As1X,
            Face: IsoFieldRebarFace.Bottom,
            Components: [component],
            ReinforcementMode: IsoFieldReinforcementMode.FullCombination);
        return new RebarRulePreviewResult(
            [new RebarRulePreviewItem("zone-a", "Zone A", rule, Array.Empty<string>(), EstimatedBarCount: 12)],
            Array.Empty<string>(),
            IsoFieldEngineeringSettings.Default,
            12);
    }

    private static IsoFieldRecognitionResult CreateRecognition(double confidence)
    {
        return new IsoFieldRecognitionResult(
            [
                new IsoFieldPolyline(
                    "zone-a",
                    [new IsoFieldPoint(0, 0), new IsoFieldPoint(1, 0), new IsoFieldPoint(1, 1)],
                    "Zone A",
                    confidence,
                    IsoFieldLayerRole.As1X)
            ],
            Array.Empty<string>());
    }

    private static IsoFieldRebarChange CreateChange(
        IsoFieldRebarChangeKind kind,
        string stableId,
        IReadOnlyList<long>? elementIds = null)
    {
        IsoFieldRebarPlanItem? plannedItem = kind == IsoFieldRebarChangeKind.Delete
            ? null
            : new IsoFieldRebarPlanItem(stableId, "sig");
        return new IsoFieldRebarChange(
            kind,
            stableId,
            plannedItem,
            elementIds ?? Array.Empty<long>());
    }
}
