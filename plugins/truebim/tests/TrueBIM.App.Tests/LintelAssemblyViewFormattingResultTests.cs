using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelAssemblyViewFormattingResultTests
{
    [Fact]
    public void BuildSummary_ReportsCreatedPartsAndWarnings()
    {
        LintelAssemblyViewFormattingResult result = new(
            true,
            false,
            true,
            true,
            2,
            ["Высотная отметка недоступна."]);

        Assert.True(result.ModelChanged);
        Assert.Equal(2, result.CreatedAnnotationCount);
        Assert.Contains("создано аннотаций — 2", result.BuildSummary());
        Assert.Contains("Высотная отметка недоступна", result.BuildSummary());
    }

    [Fact]
    public void Failed_DoesNotReportModelChange()
    {
        LintelAssemblyViewFormattingResult result = LintelAssemblyViewFormattingResult.Failed("No geometry.");

        Assert.False(result.ModelChanged);
        Assert.Equal(0, result.CreatedAnnotationCount);
    }
}
