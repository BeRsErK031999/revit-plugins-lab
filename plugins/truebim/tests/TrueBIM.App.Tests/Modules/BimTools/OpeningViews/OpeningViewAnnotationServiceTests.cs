using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewAnnotationServiceTests
{
    [Fact]
    public void ResolveTitle_PrefersTypeMarkLikeReferenceSheets()
    {
        string title = OpeningViewAnnotationService.ResolveTitle(
            "Д-2.4л",
            "Экземпляр 17",
            "Дверь металлическая",
            OpeningViewCategoryKeys.Door,
            42);

        Assert.Equal("Д-2.4л", title);
    }

    [Fact]
    public void ResolveTitle_FallsBackToCategoryAndElementId()
    {
        string title = OpeningViewAnnotationService.ResolveTitle(
            null,
            " ",
            null,
            OpeningViewCategoryKeys.Window,
            84);

        Assert.Equal("Окно 84", title);
    }

    [Fact]
    public void ResolveTitle_FallsBackToCurtainWallAndElementId()
    {
        string title = OpeningViewAnnotationService.ResolveTitle(
            null,
            " ",
            null,
            OpeningViewCategoryKeys.CurtainWall,
            126);

        Assert.Equal("Витраж 126", title);
    }

    [Fact]
    public void Metadata_NormalizesCategoryAndOwnedAnnotationIds()
    {
        OpeningViewMetadata metadata = new(
            " source ",
            123,
            "window",
            [" one ", "one", "", "two"]);

        Assert.Equal("source", metadata.SourceElementUniqueId);
        Assert.Equal(OpeningViewCategoryKeys.Window, metadata.CategoryKey);
        Assert.Equal(["one", "two"], metadata.AnnotationUniqueIds);
    }

    [Fact]
    public void Metadata_PreservesCurtainWallCategory()
    {
        OpeningViewMetadata metadata = new("source", 321, "curtainwall");

        Assert.Equal(OpeningViewCategoryKeys.CurtainWall, metadata.CategoryKey);
    }
}
