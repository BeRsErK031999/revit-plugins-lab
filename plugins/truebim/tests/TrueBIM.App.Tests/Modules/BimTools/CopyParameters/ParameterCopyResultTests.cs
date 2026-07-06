using TrueBIM.App.Modules.BimTools.CopyParameters.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.CopyParameters;

public sealed class ParameterCopyResultTests
{
    [Fact]
    public void ToDialogText_IncludesCountsAndGroupedSkipReasons()
    {
        ParameterCopyResult result = new(
            "Двери 101 [42]",
            2,
            3,
            [
                new ElementCopyReportRow("Стены A [1]", "Марка", true, "скопировано"),
                new ElementCopyReportRow("Стены B [2]", "Марка", false, "параметр отсутствует"),
                new ElementCopyReportRow("Стены C [3]", "Марка", false, "параметр отсутствует"),
                new ElementCopyReportRow("Стены C [3]", "Комментарии", false, "параметр только для чтения")
            ]);

        string text = result.ToDialogText();

        Assert.Contains("Исходный элемент: Двери 101 [42]", text);
        Assert.Contains("Выбрано параметров: 2", text);
        Assert.Contains("Выбрано элементов-получателей: 3", text);
        Assert.Contains("Успешно скопировано: 1", text);
        Assert.Contains("Пропущено: 3", text);
        Assert.Contains("- параметр отсутствует: 2", text);
        Assert.Contains("- параметр только для чтения: 1", text);
    }
}
