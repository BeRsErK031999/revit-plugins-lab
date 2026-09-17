using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;

public sealed class ScheduleRegisterTemplateInspector
{
    private readonly ScheduleRegisterTemplateValidator validator;

    public ScheduleRegisterTemplateInspector(ScheduleRegisterTemplateValidator validator)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public ScheduleRegisterTemplateInspection Inspect(Document document)
    {
        Guard.NotNull(document, nameof(document));
        ViewSchedule? schedule = FindTemplate(document);
        if (schedule is null)
        {
            return new ScheduleRegisterTemplateInspection(
                null,
                ScheduleRegisterTemplateValidation.Missing(
                    $"Спецификация «{ScheduleRegisterConstants.TemplateScheduleName}» не найдена."),
                0);
        }

        ScheduleRegisterTemplateValidation validation = Validate(schedule);
        int placementCount = new FilteredElementCollector(document)
            .OfClass(typeof(ScheduleSheetInstance))
            .Cast<ScheduleSheetInstance>()
            .Count(instance => instance.ScheduleId == schedule.Id);
        if (placementCount > 0)
        {
            validation = validation with
            {
                IsValid = false,
                Issues =
                [
                    .. validation.Issues,
                    $"Шаблон размещён на листах ({placementCount} экз.) и не должен использоваться как рабочая спецификация."
                ]
            };
        }

        return new ScheduleRegisterTemplateInspection(
            RevitElementIds.GetValue(schedule.Id),
            validation,
            placementCount);
    }

    public ViewSchedule? FindTemplate(Document document)
    {
        Guard.NotNull(document, nameof(document));
        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .FirstOrDefault(schedule =>
                !schedule.IsTemplate
                && string.Equals(
                    schedule.Name,
                    ScheduleRegisterConstants.TemplateScheduleName,
                    StringComparison.CurrentCultureIgnoreCase));
    }

    public ScheduleRegisterTemplateValidation Validate(ViewSchedule schedule)
    {
        Guard.NotNull(schedule, nameof(schedule));
        return validator.Validate(CreateSnapshot(schedule));
    }

    public static ScheduleRegisterTemplateSnapshot CreateSnapshot(ViewSchedule schedule)
    {
        Guard.NotNull(schedule, nameof(schedule));
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        header.RefreshData();

        List<IReadOnlyList<string>> rows = [];
        for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
        {
            List<string> values = [];
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                values.Add(schedule.GetCellText(SectionType.Header, row, column) ?? string.Empty);
            }

            rows.Add(values);
        }

        return new ScheduleRegisterTemplateSnapshot(rows);
    }
}
