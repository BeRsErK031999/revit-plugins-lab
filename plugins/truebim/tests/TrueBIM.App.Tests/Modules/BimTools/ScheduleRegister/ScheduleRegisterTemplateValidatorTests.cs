using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleRegister;

public sealed class ScheduleRegisterTemplateValidatorTests
{
    private readonly ScheduleRegisterTemplateValidator validator = new();

    [Fact]
    public void Validate_AcceptsExpectedHeadersAndBlankDataRows()
    {
        ScheduleRegisterTemplateValidation result = validator.Validate(CreateSnapshot(
            ["Ведомость спецификаций", "", ""],
            ["Лист", "Наименование", "Примечание"],
            ["", "", ""],
            ["", "", ""]));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.ColumnHeaderRowIndex);
        Assert.Equal(2, result.DataStartRowIndex);
        Assert.Equal(2, result.DataRowCount);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_RejectsTemplateWithFilledDataRows()
    {
        ScheduleRegisterTemplateValidation result = validator.Validate(CreateSnapshot(
            ["Ведомость спецификаций", "", ""],
            ["Лист", "Наименование", "Примечание"],
            ["3", "Спецификация материалов", ""]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("должны быть пустыми", StringComparison.CurrentCultureIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsWrongTitleOrColumns()
    {
        ScheduleRegisterTemplateValidation result = validator.Validate(CreateSnapshot(
            ["Другая таблица", "", ""],
            ["Номер", "Наименование", "Примечание"],
            ["", "", ""]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("Первая строка", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(result.Issues, issue => issue.Contains("Не найдена строка", StringComparison.CurrentCultureIgnoreCase));
    }

    private static ScheduleRegisterTemplateSnapshot CreateSnapshot(params string[][] rows)
    {
        return new ScheduleRegisterTemplateSnapshot(rows);
    }
}
