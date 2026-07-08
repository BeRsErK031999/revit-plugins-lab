using TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.TitleBlockFill;

public sealed class TitleBlockApplyResultTests
{
    [Fact]
    public void Counts_GroupRowsByStatus()
    {
        TitleBlockApplyResult result = new(
        [
            CreateRow("Готово"),
            CreateRow("Пропущено"),
            CreateRow("Ошибка")
        ]);

        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
    }

    private static TitleBlockPreviewRow CreateRow(string status)
    {
        return new TitleBlockPreviewRow(
            0,
            42,
            "A-101",
            "План",
            TitleBlockRuleTargets.Sheet,
            "Дата",
            string.Empty,
            "08.07.2026",
            status,
            string.Empty,
            CanApply: status == "Готово");
    }
}
