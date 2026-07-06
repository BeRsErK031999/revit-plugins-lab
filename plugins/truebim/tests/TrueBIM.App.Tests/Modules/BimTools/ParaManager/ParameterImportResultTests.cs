using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParaManager;

public sealed class ParameterImportResultTests
{
    [Fact]
    public void ToDialogTextIncludesSummaryAndFailedRows()
    {
        ParameterImportRow created = CreateRow("BIM_Раздел");
        created.Status = ParameterImportStatus.Created;
        ParameterImportRow updated = CreateRow("BIM_Этаж");
        updated.Status = ParameterImportStatus.Updated;
        ParameterImportRow skipped = CreateRow("BIM_Стадия");
        skipped.Status = ParameterImportStatus.Skipped;
        ParameterImportRow failed = CreateRow("BIM_Ошибка");
        failed.Status = ParameterImportStatus.Failed;
        failed.Message = "Категория не найдена";

        string text = new ParameterImportResult([created, updated, skipped, failed]).ToDialogText();

        Assert.Contains("Создано параметров: 1", text);
        Assert.Contains("Обновлено привязок: 1", text);
        Assert.Contains("Пропущено: 1", text);
        Assert.Contains("Ошибок: 1", text);
        Assert.Contains("строка 2: BIM_Ошибка - Категория не найдена", text);
    }

    private static ParameterImportRow CreateRow(string name)
    {
        return new ParameterImportRow(2, name, "BIM", "Instance", "Walls", "Identity Data", "Text", "true", "true", string.Empty);
    }
}
