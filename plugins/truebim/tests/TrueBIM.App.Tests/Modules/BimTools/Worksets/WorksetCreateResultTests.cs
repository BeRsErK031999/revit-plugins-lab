using TrueBIM.App.Modules.BimTools.Worksets.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.Worksets;

public sealed class WorksetCreateResultTests
{
    [Fact]
    public void ToDialogText_IncludesSummaryCountsAndErrors()
    {
        WorksetImportRow created = new(1, "АР_Стены", "АР_Стены")
        {
            Status = WorksetImportStatus.Created,
            Message = "Создан"
        };
        WorksetImportRow existing = new(2, "Existing", "Existing")
        {
            Status = WorksetImportStatus.Existing,
            Message = "Уже существует"
        };
        WorksetImportRow invalid = new(3, "Bad/Name", "Bad/Name")
        {
            Status = WorksetImportStatus.Invalid,
            Message = "Недопустимое имя"
        };
        WorksetImportRow failed = new(4, "Fail", "Fail")
        {
            Status = WorksetImportStatus.Failed,
            Message = "Ошибка Revit"
        };

        string text = new WorksetCreateResult([created, existing, invalid, failed]).ToDialogText();

        Assert.Contains("Создано: 1", text);
        Assert.Contains("Уже существовало: 1", text);
        Assert.Contains("Пропущено: 1", text);
        Assert.Contains("Ошибок: 1", text);
        Assert.Contains("строка 4: Fail - Ошибка Revit", text);
    }
}
