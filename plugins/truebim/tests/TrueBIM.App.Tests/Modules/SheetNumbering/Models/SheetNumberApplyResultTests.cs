using TrueBIM.App.Modules.SheetNumbering.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Models;

public sealed class SheetNumberApplyResultTests
{
    [Fact]
    public void TotalCount_ReturnsSumOfAllOutcomeCounts()
    {
        SheetNumberApplyResult result = new(
            ChangedCount: 2,
            UnchangedCount: 3,
            SkippedCount: 4,
            FailedCount: 5);

        Assert.Equal(14, result.TotalCount);
    }
}
