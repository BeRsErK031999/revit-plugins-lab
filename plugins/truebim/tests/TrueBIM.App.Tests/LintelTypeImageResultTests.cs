using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelTypeImageResultTests
{
    [Fact]
    public void BuildSummary_ReportsAssignedImageAndPixels()
    {
        LintelTypeImageResult result = new(
            true,
            true,
            @"C:\Images\lintel.png",
            1600,
            587,
            ["Назначено."]);

        Assert.True(result.ModelChanged);
        Assert.Contains("PNG — экспортирован", result.BuildSummary());
        Assert.Contains("Изображение типоразмера» — назначено", result.BuildSummary());
        Assert.Contains("1600 × 587 px", result.BuildSummary());
        Assert.Contains(@"C:\Images\lintel.png", result.BuildSummary());
    }

    [Fact]
    public void Failed_PreservesSuccessfulExportWithoutReportingModelChange()
    {
        LintelTypeImageResult result = LintelTypeImageResult.Failed(
            "Параметр недоступен.",
            true,
            @"C:\Images\lintel.png");

        Assert.True(result.ImageExported);
        Assert.False(result.TypeImageAssigned);
        Assert.False(result.ModelChanged);
        Assert.Contains("Параметр недоступен", result.BuildSummary());
    }
}
