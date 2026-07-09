using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyManagerPaneSummaryBuilderTests
{
    [Fact]
    public void Build_SummarizesOnlySelectedFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "TrueBimFamilyPane", "Library");
        DateTimeOffset cacheUpdated = new(2026, 7, 9, 8, 30, 0, TimeSpan.Zero);
        FamilyManagerProfile profile = new()
        {
            CacheUpdatedAtUtc = cacheUpdated,
            CachedFiles =
            [
                new FamilyFileItem
                {
                    FilePath = Path.Combine(root, "Doors", "Door A.rfa"),
                    Name = "Door A",
                    Category = "Двери",
                    MetadataUpdatedAtUtc = cacheUpdated,
                    IsFavorite = true,
                    CachedTypes = [new FamilyTypeInfo(0, "900x2100")]
                },
                new FamilyFileItem
                {
                    FilePath = Path.Combine(root, "Windows", "Window A.rfa"),
                    Name = "Window A",
                    Category = "Окна",
                    CachedTypes =
                    [
                        new FamilyTypeInfo(0, "900x1200"),
                        new FamilyTypeInfo(0, "1200x1500")
                    ]
                },
                new FamilyFileItem
                {
                    FilePath = Path.Combine(Path.GetTempPath(), "Other", "Chair.rfa"),
                    Name = "Chair",
                    Category = "Мебель"
                }
            ]
        };

        FamilyManagerPaneSummary summary = new FamilyManagerPaneSummaryBuilder().Build(profile, root);

        Assert.Equal("Library", summary.FolderName);
        Assert.Equal(2, summary.FamilyCount);
        Assert.Equal(2, summary.CategoryCount);
        Assert.Equal(1, summary.MetadataCount);
        Assert.Equal(3, summary.TypeCount);
        Assert.Equal(1, summary.FavoriteCount);
        Assert.Equal(cacheUpdated, summary.CacheUpdatedAtUtc);
        Assert.Equal(["Двери", "Окна"], summary.Categories);
        Assert.DoesNotContain("Chair", summary.RecentFamilies);
    }
}
