using TrueBIM.App.Modules.BimTools.AutoTags.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.AutoTags;

public sealed class AutoTagApplyResultTests
{
    [Fact]
    public void ToDialogTextIncludesCountersAndMessages()
    {
        AutoTagApplyResult result = new(
        [
            new AutoTagReportRow("Применение", "Level 1", 1, "Walls", "Basic", "Авто", AutoTagStatuses.Done, "Марка создана."),
            new AutoTagReportRow("Применение", "Level 1", 2, "Doors", "Door", "Авто", AutoTagStatuses.Skipped, "Уже есть марка."),
            new AutoTagReportRow("Применение", "Level 1", 3, "Windows", "Window", "Авто", AutoTagStatuses.Error, "Нет типа марки.")
        ]);

        string text = result.ToDialogText();

        Assert.Contains("Создано марок: 1", text);
        Assert.Contains("Пропущено: 1", text);
        Assert.Contains("Ошибок: 1", text);
        Assert.Contains("2: Уже есть марка.", text);
        Assert.Contains("3: Нет типа марки.", text);
    }
}
