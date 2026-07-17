using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRebarQualityServiceTests
{
    private readonly IsoFieldRebarQualityService service = new();

    [Fact]
    public void Analyze_AllLayersCoverHost_ReturnsNoIssues()
    {
        RebarRulePreviewItem[] items = Enum.GetValues<IsoFieldLayerRole>()
            .Select(role => CreateItem(role.ToString(), role, CreateRegion(0, 0, 10, 10)))
            .ToArray();

        IsoFieldRebarQualityResult result = service.Analyze(
            CreatePreview(items),
            CreateBinding());

        Assert.Empty(result.Issues);
        Assert.True(result.CanCompare);
        Assert.True(result.CanApply(warningsAccepted: false));
        Assert.All(result.LayerCoverage, coverage => Assert.Equal(1, coverage.CoverageRatio, 6));
    }

    [Fact]
    public void Analyze_RequiredAreaDeficitAndSameLayerOverlap_ReturnsBlockingIssues()
    {
        RebarRulePreviewItem first = CreateItem(
            "zone-a",
            IsoFieldLayerRole.As1X,
            CreateRegion(0, 0, 6, 10),
            requiredArea: 8,
            providedArea: 7.5);
        RebarRulePreviewItem second = CreateItem(
            "zone-b",
            IsoFieldLayerRole.As1X,
            CreateRegion(5, 0, 10, 10));

        IsoFieldRebarQualityResult result = service.Analyze(
            CreatePreview([first, second]),
            CreateBinding());

        Assert.Contains(result.BlockingIssues, issue => issue.Code == IsoFieldRebarQualityCode.RequiredAreaDeficit);
        IsoFieldRebarQualityIssue overlap = Assert.Single(result.BlockingIssues, issue =>
            issue.Code == IsoFieldRebarQualityCode.SameLayerOverlap);
        Assert.Equal(["zone-a", "zone-b"], overlap.EffectiveZoneIds);
        Assert.False(result.CanCompare);
        Assert.False(result.CanApply(warningsAccepted: true));
    }

    [Fact]
    public void Analyze_CrossLayerOverlapIsAllowed()
    {
        RebarRulePreviewItem first = CreateItem(
            "as1",
            IsoFieldLayerRole.As1X,
            CreateRegion(0, 0, 10, 10));
        RebarRulePreviewItem second = CreateItem(
            "as3",
            IsoFieldLayerRole.As3Y,
            CreateRegion(0, 0, 10, 10));

        IsoFieldRebarQualityResult result = service.Analyze(
            CreatePreview([first, second]),
            CreateBinding());

        Assert.DoesNotContain(result.Issues, issue => issue.Code == IsoFieldRebarQualityCode.SameLayerOverlap);
    }

    [Fact]
    public void Analyze_PartialCoverageAndClipping_RequireWarningAcceptance()
    {
        RebarRulePreviewItem item = CreateItem(
            "zone-a",
            IsoFieldLayerRole.As1X,
            CreateRegion(0, 0, 5, 10));

        IsoFieldRebarQualityResult result = service.Analyze(
            CreatePreview([item]),
            CreateBinding(clippedZoneIds: ["zone-a"], outsideZoneIds: ["source-outside"]));

        Assert.Empty(result.BlockingIssues);
        Assert.Contains(result.Warnings, issue => issue.Code == IsoFieldRebarQualityCode.PartialLayerCoverage);
        Assert.Contains(result.Warnings, issue => issue.Code == IsoFieldRebarQualityCode.MissingLayerCoverage);
        Assert.Contains(result.Warnings, issue => issue.Code == IsoFieldRebarQualityCode.ZoneClippedByHost);
        Assert.Contains(result.Warnings, issue => issue.Code == IsoFieldRebarQualityCode.SourceZoneOutsideHost);
        Assert.False(result.CanApply(warningsAccepted: false));
        Assert.True(result.CanApply(warningsAccepted: true));
    }

    [Fact]
    public void Analyze_FinalGeometryOutsideHost_ReturnsBlockingIssue()
    {
        RebarRulePreviewItem item = CreateItem(
            "outside",
            IsoFieldLayerRole.As1X,
            CreateRegion(8, 0, 12, 10));

        IsoFieldRebarQualityResult result = service.Analyze(
            CreatePreview([item]),
            CreateBinding());

        IsoFieldRebarQualityIssue issue = Assert.Single(result.BlockingIssues, candidate =>
            candidate.Code == IsoFieldRebarQualityCode.FinalGeometryOutsideHost);
        Assert.Equal(["outside"], issue.EffectiveZoneIds);
    }

    [Fact]
    public void Analyze_FingerprintIsIndependentOfPreviewItemOrder()
    {
        RebarRulePreviewItem first = CreateItem(
            "zone-a",
            IsoFieldLayerRole.As1X,
            CreateRegion(0, 0, 5, 10));
        RebarRulePreviewItem second = CreateItem(
            "zone-b",
            IsoFieldLayerRole.As1X,
            CreateRegion(5, 0, 10, 10));

        IsoFieldRebarQualityResult forward = service.Analyze(
            CreatePreview([first, second]),
            CreateBinding());
        IsoFieldRebarQualityResult reverse = service.Analyze(
            CreatePreview([second, first]),
            CreateBinding());

        Assert.Equal(forward.Fingerprint, reverse.Fingerprint);
    }

    [Fact]
    public void Analyze_InvalidHostGeometry_ReturnsBlockingDiagnostic()
    {
        IsoFieldSlabBindingAnalysis binding = CreateBinding() with
        {
            OuterBoundaryFeet = Array.Empty<IsoFieldPoint>()
        };

        IsoFieldRebarQualityResult result = service.Analyze(
            CreatePreview(
            [
                CreateItem("zone-a", IsoFieldLayerRole.As1X, CreateRegion(0, 0, 10, 10))
            ]),
            binding);

        IsoFieldRebarQualityIssue issue = Assert.Single(result.BlockingIssues);
        Assert.Equal(IsoFieldRebarQualityCode.GeometryAnalysisFailed, issue.Code);
        Assert.False(result.CanCompare);
    }

    private static RebarRulePreviewResult CreatePreview(
        IReadOnlyList<RebarRulePreviewItem> items)
    {
        return new RebarRulePreviewResult(
            items,
            Array.Empty<string>(),
            IsoFieldEngineeringSettings.Default,
            items.Sum(item => item.EstimatedBarCount),
            Array.Empty<string>());
    }

    private static RebarRulePreviewItem CreateItem(
        string zoneId,
        IsoFieldLayerRole layerRole,
        IsoFieldPolygonRegion region,
        double requiredArea = 5,
        double providedArea = 5.65)
    {
        IsoFieldRebarComponent component = new(12, 200, 0, 1);
        RebarRule rule = new(
            "Rule " + zoneId,
            "Slab",
            component.BarTypeName,
            component.SpacingMillimeters,
            PlacementDirection: IsoFieldLayerMapping.ResolveDirection(layerRole).ToString(),
            RequiredAreaSquareCentimetersPerMeter: requiredArea,
            ProvidedAreaSquareCentimetersPerMeter: providedArea,
            ReinforcementLabel: component.DisplayName,
            LayerRole: layerRole,
            Face: layerRole is IsoFieldLayerRole.As1X or IsoFieldLayerRole.As3Y
                ? IsoFieldRebarFace.Bottom
                : IsoFieldRebarFace.Top,
            Components: [component],
            ReinforcementMode: IsoFieldReinforcementMode.FullCombination);
        return new RebarRulePreviewItem(
            zoneId,
            "Zone " + zoneId,
            rule,
            Array.Empty<string>(),
            [region],
            EstimatedBarCount: 10,
            BaseDiagnostics: Array.Empty<string>());
    }

    private static IsoFieldSlabBindingAnalysis CreateBinding(
        IReadOnlyList<string>? clippedZoneIds = null,
        IReadOnlyList<string>? outsideZoneIds = null)
    {
        IReadOnlyList<IsoFieldPoint> outer = CreateLoop(0, 0, 10, 10);
        IsoFieldHostGeometry geometry = new(
            new IsoFieldRebarPoint3D(0, 0, 0),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 1, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            [outer]);
        return new IsoFieldSlabBindingAnalysis(
            new IsoFieldPlanarTransform(
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(0, 0),
                1,
                0,
                false),
            geometry,
            Array.Empty<IsoFieldPolyline>(),
            Array.Empty<IsoFieldClippedZone>(),
            outer,
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            Array.Empty<IsoFieldPoint>(),
            clippedZoneIds ?? Array.Empty<string>(),
            Array.Empty<string>(),
            outsideZoneIds ?? Array.Empty<string>(),
            outsideZoneIds?.Count ?? 0,
            1,
            0,
            10,
            true,
            Array.Empty<string>(),
            true);
    }

    private static IsoFieldPolygonRegion CreateRegion(
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        return new IsoFieldPolygonRegion(
            CreateLoop(minX, minY, maxX, maxY),
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            (maxX - minX) * (maxY - minY));
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
