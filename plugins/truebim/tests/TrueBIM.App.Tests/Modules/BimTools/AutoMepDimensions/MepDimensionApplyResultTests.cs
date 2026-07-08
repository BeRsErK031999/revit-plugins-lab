using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.AutoMepDimensions;

public sealed class MepDimensionApplyResultTests
{
    [Fact]
    public void CountsRowsByStatusAndIncludesImportantMessages()
    {
        MepDimensionApplyResult result = new(
        [
            CreateRow("Трубы-H", MepDimensionStatuses.Done, "Создано"),
            CreateRow("Трубы-V", MepDimensionStatuses.Skipped, "Недостаточно Reference"),
            CreateRow("Лотки-H", MepDimensionStatuses.Error, "Invalid references")
        ]);

        string dialogText = result.ToDialogText();

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Contains("Трубы-V: Недостаточно Reference", dialogText);
        Assert.Contains("Лотки-H: Invalid references", dialogText);
    }

    private static MepDimensionReportRow CreateRow(string candidateId, string status, string message)
    {
        return new MepDimensionReportRow(
            "Применение",
            "Level 1",
            candidateId,
            "Трубы",
            "Горизонтальные трассы",
            2,
            2,
            0,
            status,
            message);
    }
}
