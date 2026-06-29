using TrueBIM.App.Modules.SheetNumbering.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Models;

public sealed class SheetNumberApplyResultTests
{
    [Fact]
    public void TotalCount_ReturnsSumOfAllOutcomeCounts()
    {
        SheetNumberApplyResult result = new(
            Succeeded: true,
            Message: "Applied.",
            ChangedCount: 2,
            UnchangedCount: 3,
            SkippedCount: 4,
            FailedCount: 5);

        Assert.Equal(14, result.TotalCount);
    }

    [Fact]
    public void Result_StoresSuccessStateAndMessage()
    {
        SheetNumberApplyResult result = new(
            Succeeded: false,
            Message: "Rolled back.",
            ChangedCount: 0,
            UnchangedCount: 1,
            SkippedCount: 0,
            FailedCount: 2);

        Assert.False(result.Succeeded);
        Assert.Equal("Rolled back.", result.Message);
    }
}
