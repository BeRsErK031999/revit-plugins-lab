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

    [Theory]
    [InlineData(OpeningViewCategoryKeys.Door, " (проём)")]
    [InlineData(OpeningViewCategoryKeys.Window, " (проём)")]
    [InlineData(OpeningViewCategoryKeys.CurtainWall, " (габарит витража)")]
    public void ResolveDimensionSuffix_ExplainsMeasuredGeometry(string categoryKey, string expected)
    {
        Assert.Equal(expected, OpeningViewAnnotationService.ResolveDimensionSuffix(categoryKey));
    }

    [Fact]
    public void ResolveDimensionSuffix_LabelsFamilyGeometryFallbackHonestly()
    {
        Assert.Equal(
            " (габарит изделия)",
            OpeningViewAnnotationService.ResolveDimensionSuffix(
                OpeningViewCategoryKeys.Window,
                usesFamilyGeometry: true));
    }

    [Fact]
    public void AnnotationPreview_UsesCurtainWallDimensionLabels()
    {
        OpeningViewAnnotationPreview preview = new(
            "ВН-4.1",
            canCreateTitle: true,
            canCreateWidthDimension: true,
            canCreateHeightDimension: true,
            usesCurtainWallGeometry: true);

        string dialogText = preview.ToDialogText();

        Assert.Contains("Габаритная ширина витража: готов", dialogText);
        Assert.Contains("Габаритная высота витража: готов", dialogText);
        Assert.DoesNotContain("Ширина проёма", dialogText);
    }

    [Fact]
    public void AnnotationPreview_UsesOpeningDimensionLabelsByDefault()
    {
        OpeningViewAnnotationPreview preview = new(
            "ОБ-13.0",
            canCreateTitle: true,
            canCreateWidthDimension: true,
            canCreateHeightDimension: true);

        string dialogText = preview.ToDialogText();

        Assert.Contains("Ширина проёма: готов", dialogText);
        Assert.Contains("Высота проёма: готов", dialogText);
        Assert.DoesNotContain("габаритная", dialogText, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void AnnotationPreview_ExplainsFamilyGeometryFallbackAndReferenceSources()
    {
        OpeningViewAnnotationPreview preview = new(
            "ОК-27",
            canCreateTitle: true,
            canCreateWidthDimension: true,
            canCreateHeightDimension: true,
            widthUsesFamilyGeometry: true,
            widthReferenceSource: "крайние видимые грани изделия",
            heightReferenceSource: "стандартные planes семейства");

        string dialogText = preview.ToDialogText();

        Assert.Contains("Габаритная ширина изделия: готов — крайние видимые грани изделия", dialogText);
        Assert.Contains("Высота проёма: готов — стандартные planes семейства", dialogText);
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
