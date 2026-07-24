using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelAssemblyViewCreationResultTests
{
    [Fact]
    public void CreatedResult_ReportsModelChangeAndElementId()
    {
        LintelAssemblyViewCreationResult result = new(
            LintelAssemblyViewCreationStatus.Created,
            "Assembly",
            "Side view",
            321,
            "Created at 1:10.");

        Assert.True(result.ModelChanged);
        Assert.Equal("Создано", result.StatusDisplay);
        Assert.Contains("ElementId: 321", result.BuildSummary());
    }

    [Theory]
    [InlineData(LintelAssemblyViewCreationStatus.AlreadyExists, "Уже существует")]
    [InlineData(LintelAssemblyViewCreationStatus.Blocked, "Заблокировано")]
    [InlineData(LintelAssemblyViewCreationStatus.Failed, "Ошибка")]
    public void NonCreatedResults_DoNotReportModelChange(
        LintelAssemblyViewCreationStatus status,
        string expectedDisplay)
    {
        LintelAssemblyViewCreationResult result = new(
            status,
            "Assembly",
            "Side view",
            null,
            "No change.");

        Assert.False(result.ModelChanged);
        Assert.Equal(expectedDisplay, result.StatusDisplay);
    }

    [Fact]
    public void ExistingViewWithFormatting_ReportsModelChangeAndFormattingSummary()
    {
        LintelAssemblyViewFormattingResult formatting = new(
            true,
            true,
            false,
            true,
            0,
            ["Frame family missing."]);
        LintelAssemblyViewCreationResult result = new(
            LintelAssemblyViewCreationStatus.AlreadyExists,
            "Assembly",
            "Side view",
            321,
            "Existing view formatted.",
            formatting);

        Assert.True(result.ModelChanged);
        Assert.Contains("Оформление:", result.BuildSummary());
    }

    [Fact]
    public void ExistingViewWithTypeImage_ReportsAssignmentSummary()
    {
        LintelTypeImageResult typeImage = new(
            true,
            true,
            @"C:\Images\lintel.png",
            1600,
            587,
            []);
        LintelAssemblyViewCreationResult result = new(
            LintelAssemblyViewCreationStatus.AlreadyExists,
            "Assembly",
            "Side view",
            321,
            "Existing view image updated.",
            null,
            typeImage);

        Assert.True(result.ModelChanged);
        Assert.Contains("Изображение типоразмера", result.BuildSummary());
        Assert.Contains(@"C:\Images\lintel.png", result.BuildSummary());
    }
}
