using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashTriageServiceTests
{
    [Fact]
    public void Create_BuildsStableFingerprintIndependentOfPairOrder()
    {
        ClashTriageService service = new();
        ClashTriageInput first = new(
            "Current model",
            101,
            202,
            null,
            null,
            "Walls",
            "Pipes",
            1,
            2,
            3,
            9_000_000,
            ClashType.Hard,
            ClashGroupingStrategy.SourceCategoryPair);
        ClashTriageInput second = first with
        {
            ElementId1 = 202,
            ElementId2 = 101,
            Element1Category = "Pipes",
            Element2Category = "Walls"
        };

        ClashTriageResult firstResult = service.Create(first);
        ClashTriageResult secondResult = service.Create(second);

        Assert.Equal(firstResult.Fingerprint, secondResult.Fingerprint);
        Assert.Equal("Current model | Pipes x Walls", firstResult.GroupKey);
        Assert.Equal(ClashPriority.High, firstResult.Priority);
        Assert.True(firstResult.SeverityScore > 0);
    }

    [Theory]
    [InlineData(250_000, ClashPriority.Low)]
    [InlineData(1_000_000, ClashPriority.Medium)]
    [InlineData(8_000_000, ClashPriority.High)]
    [InlineData(125_000_000, ClashPriority.Critical)]
    public void ResolvePriority_UsesVolumeBuckets(double volumeMm3, ClashPriority expected)
    {
        Assert.Equal(expected, ClashTriageService.ResolvePriority(volumeMm3));
    }

    [Fact]
    public void Create_UsesLocationBucketGroupingWhenRequested()
    {
        ClashTriageService service = new();
        ClashTriageInput input = new(
            "RVT links",
            11,
            22,
            111,
            222,
            "Ducts",
            "Floors",
            1.2,
            2.4,
            0.5,
            2_000_000,
            ClashType.Clearance,
            ClashGroupingStrategy.LocationBucket);

        ClashTriageResult result = service.Create(input);

        Assert.StartsWith("RVT links | Grid", result.GroupKey);
        Assert.StartsWith("CM-", result.Fingerprint);
    }
}
