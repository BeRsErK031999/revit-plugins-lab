using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ColorByParameter;

public sealed class ColorApplyResultTests
{
    [Fact]
    public void ToDialogTextIncludesCountsAndMessages()
    {
        ColorApplyResult result = new(
            createdFilterCount: 2,
            updatedFilterCount: 1,
            appliedFilterCount: 3,
            skippedValueCount: 4,
            clearedFilterCount: 5,
            messages: ["Параметр недоступен"]);

        string text = result.ToDialogText();

        Assert.Contains("Создано фильтров: 2", text);
        Assert.Contains("Обновлено фильтров: 1", text);
        Assert.Contains("Применено к активному виду: 3", text);
        Assert.Contains("Пропущено значений: 4", text);
        Assert.Contains("Очищено фильтров с активного вида: 5", text);
        Assert.Contains("Параметр недоступен", text);
    }
}
