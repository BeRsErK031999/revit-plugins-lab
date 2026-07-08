using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.DatumExtents;

public sealed class DatumExtentApplyResultTests
{
    [Fact]
    public void ToDialogTextIncludesCountersAndMessages()
    {
        DatumExtentApplyResult result = new(
        [
            new DatumExtentReportRow("Применение", "Level 1", 1, "Ось", "A", "2D", "2D", "2D", DatumExtentStatuses.Done, "Изменено."),
            new DatumExtentReportRow("Применение", "Level 1", 2, "Ось", "B", "2D", "2D", "2D", DatumExtentStatuses.Unchanged, "Без изменений."),
            new DatumExtentReportRow("Применение", "Level 1", 3, "Уровень", "Level 2", "2D", "3D", "3D", DatumExtentStatuses.Error, "Недоступно.")
        ]);

        string text = result.ToDialogText();

        Assert.Contains("Изменено datum-элементов: 1", text);
        Assert.Contains("Без изменений: 1", text);
        Assert.Contains("Ошибок: 1", text);
        Assert.Contains("Level 2: Недоступно.", text);
    }
}
