using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class RebarRuleValidationServiceTests
{
    private readonly RebarRuleValidationService service = new();

    [Fact]
    public void BuildPreview_RequiresHostElement()
    {
        IsoFieldRecognitionResult recognitionResult = CreateRecognitionResult(confidence: 0.9);

        RebarRulePreviewResult result = service.BuildPreview(recognitionResult, hostElement: null);

        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Выберите стену", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPreview_CreatesRuleForEachPolyline()
    {
        IsoFieldRecognitionResult recognitionResult = CreateRecognitionResult(confidence: 0.9);
        IsoFieldHostElement host = new(42, "Wall", "Стена", "Basic Wall");

        RebarRulePreviewResult result = service.BuildPreview(recognitionResult, host);

        RebarRulePreviewItem item = Assert.Single(result.Items);
        Assert.Empty(result.Diagnostics);
        Assert.True(item.IsValid);
        Assert.Equal("Wall", item.Rule.HostKind);
        Assert.Equal("Ø12 A500", item.Rule.BarTypeName);
        Assert.Equal(100, item.Rule.SpacingMillimeters);
        Assert.Equal("AlongHost", item.Rule.PlacementDirection);
    }

    [Fact]
    public void BuildPreview_CreatesSlabRuleWithAutoPlacementDirection()
    {
        IsoFieldRecognitionResult recognitionResult = CreateRecognitionResult(confidence: 0.7);
        IsoFieldHostElement host = new(42, "Slab", "Плита", "Floor 200");

        RebarRulePreviewResult result = service.BuildPreview(recognitionResult, host);

        RebarRulePreviewItem item = Assert.Single(result.Items);
        Assert.True(item.IsValid);
        Assert.Equal("Slab", item.Rule.HostKind);
        Assert.Equal("Ø10 A500", item.Rule.BarTypeName);
        Assert.Equal(150, item.Rule.SpacingMillimeters);
        Assert.Equal("Auto", item.Rule.PlacementDirection);
    }

    [Fact]
    public void ValidateRule_RejectsUnsupportedHostAndSpacing()
    {
        RebarRule rule = new("Bad rule", "Column", string.Empty, 25, PlacementDirection: "Diagonal");

        IReadOnlyList<string> diagnostics = service.ValidateRule(rule);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("HostKind", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("Тип арматуры", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("50-400", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("Направление", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPreview_UsesUpperLegendBoundaryAndClippedRegionsForSlab()
    {
        IsoFieldRecognitionResult recognition = CreateEngineeringRecognition(maximumValue: 7.85);
        IsoFieldHostElement host = CreateSlabHost();
        IsoFieldSlabBindingAnalysis binding = CreateBinding(recognition, host.Geometry!);

        RebarRulePreviewResult result = service.BuildPreview(
            recognition,
            host,
            CreateSourceSet(),
            binding,
            new IsoFieldEngineeringSettings(
                IsoFieldReinforcementMode.AdditionalOverBase,
                ConcreteCoverMillimeters: 30,
                BoundaryOffsetMillimeters: 0,
                MinimumBarLengthMillimeters: 100));

        RebarRulePreviewItem item = Assert.Single(result.Items);
        Assert.True(result.CanCreateRebar, string.Join(Environment.NewLine, result.Diagnostics.Concat(item.Diagnostics)));
        Assert.True(result.IsEngineeringPreview);
        Assert.Equal(7.85, item.Rule.RequiredAreaSquareCentimetersPerMeter!.Value);
        Assert.Equal(7.854, item.Rule.ProvidedAreaSquareCentimetersPerMeter!.Value, 3);
        Assert.Equal("d10s200+d10s200", item.Rule.ReinforcementLabel);
        Assert.Equal(IsoFieldLayerRole.As1X, item.Rule.LayerRole);
        Assert.Equal(IsoFieldRebarFace.Bottom, item.Rule.Face);
        IsoFieldRebarComponent component = Assert.Single(item.Rule.EffectiveComponents);
        Assert.Equal(1, component.CombinationIndex);
        Assert.NotEmpty(item.EffectiveRegions);
        Assert.True(item.EstimatedBarCount > 0);
        Assert.Equal(item.EstimatedBarCount, result.EstimatedBarCount);
    }

    [Fact]
    public void BuildPreview_BlocksCombinationBelowUpperLegendBoundary()
    {
        IsoFieldRecognitionResult recognition = CreateEngineeringRecognition(maximumValue: 8.1);
        IsoFieldHostElement host = CreateSlabHost();

        RebarRulePreviewResult result = service.BuildPreview(
            recognition,
            host,
            CreateSourceSet(),
            CreateBinding(recognition, host.Geometry!),
            IsoFieldEngineeringSettings.Default);

        RebarRulePreviewItem item = Assert.Single(result.Items);
        Assert.False(result.CanCreateRebar);
        Assert.Contains(item.Diagnostics, diagnostic =>
            diagnostic.Contains("меньше требуемой", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPreview_BlocksSlabEngineeringRulesForJsonOnlySource()
    {
        IsoFieldRecognitionResult recognition = CreateEngineeringRecognition(maximumValue: 7.85);
        IsoFieldHostElement host = CreateSlabHost();

        RebarRulePreviewResult result = service.BuildPreview(
            recognition,
            host,
            sourceSet: null,
            slabBinding: CreateBinding(recognition, host.Geometry!),
            engineeringSettings: IsoFieldEngineeringSettings.Default);

        Assert.False(result.CanCreateRebar);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("четырёх карт", StringComparison.Ordinal));
    }

    private static IsoFieldRecognitionResult CreateRecognitionResult(double confidence)
    {
        return new IsoFieldRecognitionResult(
            [
                new IsoFieldPolyline(
                    "zone-a",
                    [
                        new IsoFieldPoint(0, 0),
                        new IsoFieldPoint(10, 0),
                        new IsoFieldPoint(10, 10)
                    ],
                    "Zone A",
                    confidence)
            ],
            Array.Empty<string>());
    }

    private static IsoFieldRecognitionResult CreateEngineeringRecognition(double maximumValue)
    {
        return new IsoFieldRecognitionResult(
            [
                new IsoFieldPolyline(
                    "As1X:zone-a",
                    CreateLoop(1, 1, 9, 9),
                    "Zone A",
                    0.95,
                    IsoFieldLayerRole.As1X,
                    LegendBandIndex: 0)
            ],
            Array.Empty<string>(),
            [
                new IsoFieldLegend(
                    [
                        new IsoFieldLegendBand(
                            0,
                            255,
                            255,
                            0,
                            0,
                            1,
                            MinimumValue: 0.5,
                            MaximumValue: maximumValue)
                    ],
                    PixelY: 0,
                    PixelStartX: 0,
                    PixelEndX: 100,
                    LayerRole: IsoFieldLayerRole.As1X,
                    Boundaries:
                    [
                        new IsoFieldLegendBoundary(0, 0, 0.5, "d10s200"),
                        new IsoFieldLegendBoundary(1, 1, maximumValue, "d10s200+d10s200")
                    ])
            ]);
    }

    private static IsoFieldSourceSet CreateSourceSet()
    {
        IsoFieldSourceFile[] files = IsoFieldSourceSet.RequiredRoles
            .Select(role => new IsoFieldSourceFile($"C:\\maps\\{role}.png", role, 100, 100))
            .ToArray();
        IsoFieldLayerMapping[] mappings =
        [
            new IsoFieldLayerMapping(IsoFieldLayerRole.As1X, IsoFieldRebarDirection.X, IsoFieldRebarFace.Bottom),
            new IsoFieldLayerMapping(IsoFieldLayerRole.As2X, IsoFieldRebarDirection.X, IsoFieldRebarFace.Top),
            new IsoFieldLayerMapping(IsoFieldLayerRole.As3Y, IsoFieldRebarDirection.Y, IsoFieldRebarFace.Bottom),
            new IsoFieldLayerMapping(IsoFieldLayerRole.As4Y, IsoFieldRebarDirection.Y, IsoFieldRebarFace.Top)
        ];
        return new IsoFieldSourceSet(files, mappings);
    }

    private static IsoFieldHostElement CreateSlabHost()
    {
        IsoFieldHostGeometry geometry = new(
            new IsoFieldRebarPoint3D(0, 0, 10),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 1, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            [CreateLoop(0, 0, 10, 10)]);
        return new IsoFieldHostElement(42, "Slab", "Плита", "Floor 200", geometry);
    }

    private static IsoFieldSlabBindingAnalysis CreateBinding(
        IsoFieldRecognitionResult recognition,
        IsoFieldHostGeometry geometry)
    {
        return new IsoFieldSlabBindingService().Analyze(
            recognition,
            geometry,
            new IsoFieldSlabBindingInput(
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(10, 0),
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(10, 0),
                MirrorImageY: false,
                ImagePoint3: new IsoFieldPoint(0, 10),
                HostPoint3Feet: new IsoFieldPoint(0, 10)));
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
