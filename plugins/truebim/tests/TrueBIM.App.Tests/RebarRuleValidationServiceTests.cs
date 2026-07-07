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
}
