using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRebarReportServiceTests
{
    private readonly IsoFieldRebarReportService service = new();

    [Fact]
    public void Build_CapturesSourceHashZonesAndLayerTotals()
    {
        using TempDirectory temp = new();
        string sourcePath = Path.Combine(temp.Path, "As1X.png");
        File.WriteAllText(sourcePath, "iso source", Encoding.UTF8);
        RebarRulePreviewItem included = CreateItem("zone-a", isIncluded: true, estimatedBarCount: 12, areaSquareFeet: 10);
        RebarRulePreviewItem excluded = CreateItem("zone-b", isIncluded: false, estimatedBarCount: 0, areaSquareFeet: 5);
        IsoFieldRebarReportRequest request = CreateRequest(sourcePath, [included, excluded]);

        IsoFieldRebarReport report = service.Build(request);

        Assert.Equal(IsoFieldRebarReportService.SchemaVersion, report.SchemaVersion);
        Assert.Equal("Встроенный", report.Provenance.RecognitionRunner);
        IsoFieldRebarReportSourceFile source = Assert.Single(report.Provenance.SourceFiles);
        Assert.Equal("Готов", source.Status);
        Assert.Equal(CalculateSha256(sourcePath), source.Sha256);
        Assert.Equal(2, report.Zones.Count);
        Assert.Equal(0.82, report.Zones.Single(zone => zone.ZoneId == "zone-a").Confidence);
        IsoFieldRebarReportLayerTotal total = Assert.Single(report.LayerTotals);
        Assert.Equal(IsoFieldLayerRole.As1X, total.LayerRole);
        Assert.Equal(2, total.ZoneCount);
        Assert.Equal(1, total.IncludedZoneCount);
        Assert.Equal(1, total.ExcludedZoneCount);
        Assert.Equal(12, total.EstimatedBarCount);
        Assert.Equal(10 * 0.09290304, total.IncludedGeometryAreaSquareMeters, 8);
        Assert.False(report.ChangeSummary.Compared);
        Assert.Equal(64, report.RuleProfileSha256.Length);
    }

    [Fact]
    public void Save_WritesReadableJsonAndUtf8CsvSections()
    {
        using TempDirectory temp = new();
        string sourcePath = Path.Combine(temp.Path, "As1X.png");
        File.WriteAllText(sourcePath, "iso source", Encoding.UTF8);
        IsoFieldRebarReport report = service.Build(
            CreateRequest(sourcePath, [CreateItem("zone-a", true, 12, 10)]));

        IsoFieldRebarReportSaveResult result = service.Save(
            report,
            Path.Combine(temp.Path, "result.output"));

        Assert.EndsWith("result.json", result.JsonPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("result.csv", result.CsvPath, StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(result.JsonPath, Encoding.UTF8));
        Assert.Equal("1.2", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("As1X", document.RootElement
            .GetProperty("zones")[0]
            .GetProperty("layerRole")
            .GetString());
        byte[] csvBytes = File.ReadAllBytes(result.CsvPath);
        Assert.Equal([0xEF, 0xBB, 0xBF], csvBytes.Take(3).ToArray());
        string csv = Encoding.UTF8.GetString(csvBytes, 3, csvBytes.Length - 3);
        Assert.Contains("МЕТАДАННЫЕ", csv, StringComparison.Ordinal);
        Assert.Contains("ИСТОЧНИКИ", csv, StringComparison.Ordinal);
        Assert.Contains("ЗОНЫ", csv, StringComparison.Ordinal);
        Assert.Contains("ИТОГИ ПО СЛОЯМ", csv, StringComparison.Ordinal);
        Assert.Contains("КОНТРОЛЬ КАЧЕСТВА", csv, StringComparison.Ordinal);
        Assert.Contains("ПОКРЫТИЕ СЛОЁВ", csv, StringComparison.Ordinal);
        Assert.Contains("zone-a", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RecordsQualityIssuesAndWarningAcceptance()
    {
        using TempDirectory temp = new();
        string sourcePath = Path.Combine(temp.Path, "As1X.png");
        File.WriteAllText(sourcePath, "iso source", Encoding.UTF8);
        IsoFieldRebarQualityResult quality = new(
        [
            new IsoFieldRebarQualityIssue(
                IsoFieldRebarQualityCode.PartialLayerCoverage,
                IsoFieldRebarQualitySeverity.Warning,
                "Слой As1X покрывает 50%.",
                IsoFieldLayerRole.As1X,
                ["zone-a"],
                0.5,
                0.995)
        ],
        [
            new IsoFieldRebarLayerCoverage(IsoFieldLayerRole.As1X, 1, 5, 10, 0.5)
        ],
        "quality-fingerprint");
        IsoFieldRebarReportRequest request = CreateRequest(
            sourcePath,
            [CreateItem("zone-a", true, 12, 10)]) with
        {
            QualityResult = quality,
            QualityWarningsAccepted = true
        };

        IsoFieldRebarReport report = service.Build(request);

        Assert.True(report.QualityCheck.Evaluated);
        Assert.Equal(0, report.QualityCheck.BlockingErrorCount);
        Assert.Equal(1, report.QualityCheck.WarningCount);
        Assert.True(report.QualityCheck.WarningsAccepted);
        Assert.Equal("quality-fingerprint", report.QualityCheck.Fingerprint);
        Assert.Single(report.QualityCheck.Issues);
        Assert.Contains("покрывает 50%", report.Diagnostics.Single(message =>
            message.Contains("покрывает", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RecordsApplicationCountsAndElementIds()
    {
        using TempDirectory temp = new();
        string sourcePath = Path.Combine(temp.Path, "As1X.png");
        File.WriteAllText(sourcePath, "iso source", Encoding.UTF8);
        DateTimeOffset completedAt = DateTimeOffset.Parse(
            "2026-07-17T11:30:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        IsoFieldRebarReportRequest request = CreateRequest(
            sourcePath,
            [CreateItem("zone-a", true, 12, 10)]) with
        {
            ApplicationResult = new IsoFieldRebarCreationResult(
                AddedCount: 2,
                UpdatedCount: 1,
                DeletedCount: 3,
                UnchangedCount: 4,
                CreatedElementIds: [103, 101, 102],
                DeletedElementIds: [202, 201],
                Message: "Готово"),
            ApplicationCompletedAtUtc = completedAt
        };

        IsoFieldRebarReport report = service.Build(request);

        Assert.True(report.ApplicationSummary.Applied);
        Assert.Equal(completedAt, report.ApplicationSummary.CompletedAtUtc);
        Assert.Equal(2, report.ApplicationSummary.AddedCount);
        Assert.Equal(1, report.ApplicationSummary.UpdatedCount);
        Assert.Equal(3, report.ApplicationSummary.DeletedCount);
        Assert.Equal(4, report.ApplicationSummary.UnchangedCount);
        Assert.Equal([101, 102, 103], report.ApplicationSummary.CreatedElementIds);
        Assert.Equal([201, 202], report.ApplicationSummary.DeletedElementIds);
        string csv = service.FormatCsv(report);
        Assert.Contains("applicationApplied;Да", csv, StringComparison.Ordinal);
        Assert.Contains("applicationCreatedElementIds;101,102,103", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UsesRuleFingerprintIndependentOfPreviewItemOrder()
    {
        using TempDirectory temp = new();
        string sourcePath = Path.Combine(temp.Path, "As1X.png");
        File.WriteAllText(sourcePath, "iso source", Encoding.UTF8);
        RebarRulePreviewItem first = CreateItem("zone-a", true, 12, 10);
        RebarRulePreviewItem second = CreateItem("zone-b", true, 8, 6);

        IsoFieldRebarReport forward = service.Build(CreateRequest(sourcePath, [first, second]));
        IsoFieldRebarReport reverse = service.Build(CreateRequest(sourcePath, [second, first]));

        Assert.Equal(forward.RuleProfileSha256, reverse.RuleProfileSha256);
        Assert.Equal(["zone-a", "zone-b"], forward.Zones.Select(zone => zone.ZoneId));
        Assert.Equal(["zone-a", "zone-b"], reverse.Zones.Select(zone => zone.ZoneId));
    }

    [Fact]
    public void Build_RecordsComparisonSummaryAndMissingSourceDiagnostic()
    {
        using TempDirectory temp = new();
        string missingSourcePath = Path.Combine(temp.Path, "missing-As1X.png");
        IsoFieldRebarReportRequest request = CreateRequest(
            missingSourcePath,
            [CreateItem("zone-a", true, 12, 10)]) with
        {
            ChangePlan = new IsoFieldRebarChangePlan(
            [
                new IsoFieldRebarChange(
                    IsoFieldRebarChangeKind.Add,
                    "new-bar",
                    new IsoFieldRebarPlanItem("new-bar", "signature"),
                    Array.Empty<long>()),
                new IsoFieldRebarChange(
                    IsoFieldRebarChangeKind.Delete,
                    "old-bar",
                    null,
                    [101, 102])
            ],
            Array.Empty<string>())
        };

        IsoFieldRebarReport report = service.Build(request);

        Assert.True(report.ChangeSummary.Compared);
        Assert.True(report.ChangeSummary.CanApply);
        Assert.Equal(1, report.ChangeSummary.AddCount);
        Assert.Equal(2, report.ChangeSummary.DeleteCount);
        Assert.Equal("Файл отсутствует", Assert.Single(report.Provenance.SourceFiles).Status);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Contains("Файл отсутствует", StringComparison.Ordinal));
    }

    private static IsoFieldRebarReportRequest CreateRequest(
        string sourcePath,
        IReadOnlyList<RebarRulePreviewItem> items)
    {
        IsoFieldEngineeringSettings settings = new(
            IsoFieldReinforcementMode.FullCombination,
            ConcreteCoverMillimeters: 30,
            BoundaryOffsetMillimeters: 25,
            MinimumBarLengthMillimeters: 300);
        RebarRulePreviewResult preview = new(
            items,
            Array.Empty<string>(),
            settings,
            items.Sum(item => item.EstimatedBarCount),
            Array.Empty<string>());
        IsoFieldRecognitionResult recognition = new(
            items.Select(item => new IsoFieldPolyline(
                item.ZoneId,
                item.EffectiveRegions[0].OuterBoundaryFeet,
                item.ZoneName,
                item.ZoneId == "zone-a" ? 0.82 : 0.76,
                item.Rule.LayerRole)).ToArray(),
            Array.Empty<string>());
        return new IsoFieldRebarReportRequest(
            "Project.rvt",
            "document-key",
            new IsoFieldHostElement(42, "Slab", "Плита", "Перекрытие 1"),
            preview,
            recognition,
            [new IsoFieldRebarReportSourceInput(sourcePath, IsoFieldLayerRole.As1X, 1600, 900)],
            "ImageSourceSet",
            "Встроенный",
            "1.0.0.0",
            "1.0.0.0",
            IsoFieldCalibration.Default,
            GeneratedAtUtc: DateTimeOffset.Parse("2026-07-17T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static RebarRulePreviewItem CreateItem(
        string zoneId,
        bool isIncluded,
        int estimatedBarCount,
        double areaSquareFeet)
    {
        IsoFieldRebarComponent component = new(12, 200, 0, 1);
        RebarRule rule = new(
            "Rule " + zoneId,
            "Slab",
            component.BarTypeName,
            component.SpacingMillimeters,
            PlacementDirection: "X",
            RequiredAreaSquareCentimetersPerMeter: 5,
            ProvidedAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
            ReinforcementLabel: component.DisplayName,
            LayerRole: IsoFieldLayerRole.As1X,
            Face: IsoFieldRebarFace.Bottom,
            Components: [component],
            ReinforcementMode: IsoFieldReinforcementMode.FullCombination);
        IsoFieldPolygonRegion region = new(
            [
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(areaSquareFeet, 0),
                new IsoFieldPoint(areaSquareFeet, 1),
                new IsoFieldPoint(0, 1),
                new IsoFieldPoint(0, 0)
            ],
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            areaSquareFeet);
        return new RebarRulePreviewItem(
            zoneId,
            "Zone " + zoneId,
            rule,
            Array.Empty<string>(),
            [region],
            estimatedBarCount,
            Array.Empty<string>(),
            isIncluded);
    }

    private static string CalculateSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha256 = SHA256.Create();
        return string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "truebim-isofield-report-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
