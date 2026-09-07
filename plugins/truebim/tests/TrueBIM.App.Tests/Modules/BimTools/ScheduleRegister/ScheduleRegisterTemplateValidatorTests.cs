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
        Assert.True(result.HasValidStructure);
        Assert.False(result.HasFilledDataRows);
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
        Assert.True(result.HasValidStructure);
        Assert.True(result.HasFilledDataRows);
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
        Assert.False(result.HasValidStructure);
        Assert.False(result.HasFilledDataRows);
        Assert.Contains(result.Issues, issue => issue.Contains("Первая строка", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(result.Issues, issue => issue.Contains("Не найдена строка", StringComparison.CurrentCultureIgnoreCase));
    }

    [Fact]
    public void Inspection_AllowsLocalRepairWhenOnlyDataRowsAreFilled()
    {
        ScheduleRegisterTemplateValidation validation = validator.Validate(CreateSnapshot(
            ["Ведомость спецификаций", "", ""],
            ["Лист", "Наименование", "Примечание"],
            ["3", "Спецификация материалов", ""]));

        ScheduleRegisterTemplateInspection inspection = new(10, validation, 0);

        Assert.True(inspection.CanRepairLocally);
        Assert.False(inspection.IsValid);
    }

    [Fact]
    public void Inspection_AllowsLocalRepairWhenStructurallyValidTemplateIsPlacedOnSheet()
    {
        ScheduleRegisterTemplateValidation validation = validator.Validate(CreateSnapshot(
            ["Ведомость спецификаций", "", ""],
            ["Лист", "Наименование", "Примечание"],
            ["", "", ""]));

        ScheduleRegisterTemplateInspection inspection = new(10, validation, 1);

        Assert.True(inspection.CanRepairLocally);
        Assert.False(inspection.IsValid);
    }

    [Fact]
    public void Inspection_RejectsLocalRepairForBrokenStructure()
    {
        ScheduleRegisterTemplateValidation validation = validator.Validate(CreateSnapshot(
            ["Другая таблица", "", ""],
            ["Номер", "Наименование", "Примечание"],
            ["", "", ""]));

        ScheduleRegisterTemplateInspection inspection = new(10, validation, 0);

        Assert.False(inspection.CanRepairLocally);
    }

    private static ScheduleRegisterTemplateSnapshot CreateSnapshot(params string[][] rows)
    {
        return new ScheduleRegisterTemplateSnapshot(rows);
    }
}
