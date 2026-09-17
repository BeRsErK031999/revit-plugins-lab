using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;

public sealed class ScheduleRegisterTemplateRepairService
{
    private readonly ScheduleRegisterTemplateInspector inspector;
    private readonly ITrueBimLogger logger;

    public ScheduleRegisterTemplateRepairService(
        ScheduleRegisterTemplateInspector inspector,
        ITrueBimLogger logger)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ViewSchedule Repair(Document document, ViewSchedule damagedTemplate)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(damagedTemplate, nameof(damagedTemplate));
        ScheduleRegisterTemplateValidation validation = inspector.Validate(damagedTemplate);
        if (!validation.HasValidStructure)
        {
            throw new InvalidOperationException(
                "Локальное восстановление невозможно: повреждены заголовок или структура столбцов шаблона.");
        }

        using Transaction transaction = new(document, "TrueBIM: очистить шаблон ведомости спецификаций");
        transaction.Start();
        if (!damagedTemplate.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
        {
            transaction.RollBack();
            throw new InvalidOperationException("Revit не разрешает создать чистую копию шаблонной спецификации.");
        }

        ElementId cleanId = damagedTemplate.Duplicate(ViewDuplicateOption.Duplicate);
        ViewSchedule cleanTemplate = document.GetElement(cleanId) as ViewSchedule
                                     ?? throw new InvalidOperationException("Revit не вернул копию шаблонной спецификации.");
        ClearDataRows(cleanTemplate, validation);
        document.Delete(damagedTemplate.Id);
        cleanTemplate.Name = ScheduleRegisterConstants.TemplateScheduleName;

        ScheduleRegisterTemplateValidation cleanValidation = inspector.Validate(cleanTemplate);
        if (!cleanValidation.IsValid)
        {
            transaction.RollBack();
            throw new InvalidOperationException(
                "Автоматически очищенный шаблон не прошёл контрольную проверку: "
                + string.Join(" ", cleanValidation.Issues));
        }

        transaction.Commit();
        logger.Info(
            $"Schedule Register repaired local template in '{document.Title}'. "
            + $"ClearedRows={validation.DataRowCount}.");
        return cleanTemplate;
    }

    private static void ClearDataRows(
        ViewSchedule schedule,
        ScheduleRegisterTemplateValidation validation)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        header.RefreshData();
        int firstDataRow = header.FirstRowNumber + validation.DataStartRowIndex;
        for (int row = firstDataRow; row <= header.LastRowNumber; row++)
        {
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                header.SetCellText(row, column, string.Empty);
            }
        }
    }
}
