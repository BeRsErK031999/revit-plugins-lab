using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewApplyResultTests
{
    [Fact]
    public void CountsRowsByStatusAndIncludesImportantMessages()
    {
        OpeningViewApplyResult result = new(
        [
            CreateRow(1, OpeningViewStatuses.Created, "Создано"),
            CreateRow(2, OpeningViewStatuses.Skipped, "Вид уже существует"),
            CreateRow(3, OpeningViewStatuses.Error, "Elevation error")
        ]);

        string dialogText = result.ToDialogText();

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Contains("2: Вид уже существует", dialogText);
        Assert.Contains("3: Elevation error", dialogText);
    }

    private static OpeningViewReportRow CreateRow(long elementId, string status, string message)
    {
        return new OpeningViewReportRow(
            "Применение",
            "Level 1",
            elementId,
            "Дверь",
            "Family",
            "Type",
            "Level 1",
            $"BIM_Opening_Door_{elementId}",
            status,
            message);
    }
}
