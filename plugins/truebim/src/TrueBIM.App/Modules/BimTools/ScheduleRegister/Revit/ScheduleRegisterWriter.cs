using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;

public sealed class ScheduleRegisterWriter
{
    private readonly ScheduleRegisterTemplateInspector inspector;
    private readonly ITrueBimLogger logger;

    public ScheduleRegisterWriter(
        ScheduleRegisterTemplateInspector inspector,
        ITrueBimLogger logger)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ScheduleRegisterCreationResult Create(
        Document document,
        ViewSchedule template,
        IReadOnlyList<ScheduleRegisterRow> rows,
        IReadOnlyList<string> warnings)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(template, nameof(template));
        Guard.NotNull(rows, nameof(rows));
        Guard.NotNull(warnings, nameof(warnings));
        if (rows.Count == 0)
        {
            throw new ArgumentException("Ведомость нельзя создать без строк.", nameof(rows));
        }

        ScheduleRegisterTemplateValidation validation = inspector.Validate(template);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Шаблон ведомости спецификаций повреждён: " + string.Join(" ", validation.Issues));
        }

        string uniqueName = ScheduleRegisterNameService.CreateUniqueName(
            new FilteredElementCollector(document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Select(schedule => schedule.Name));

        using Transaction transaction = new(document, "TrueBIM: создать ведомость спецификаций");
        transaction.Start();
        if (!template.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
        {
            transaction.RollBack();
            throw new InvalidOperationException("Revit не разрешает копировать шаблонную спецификацию.");
        }

        ElementId duplicatedId = template.Duplicate(ViewDuplicateOption.Duplicate);
        ViewSchedule duplicated = document.GetElement(duplicatedId) as ViewSchedule
                                  ?? throw new InvalidOperationException("Revit не вернул копию шаблонной спецификации.");
        duplicated.Name = uniqueName;
        WriteRows(duplicated, validation, rows);
        transaction.Commit();

        logger.Info(
            $"Schedule Register created '{uniqueName}' with {rows.Count} rows in '{document.Title}'. Warnings={warnings.Count}.");
        return new ScheduleRegisterCreationResult(
            RevitElementIds.GetValue(duplicated.Id),
            duplicated.Name,
            rows.Count,
            warnings);
    }

    private static void WriteRows(
        ViewSchedule schedule,
        ScheduleRegisterTemplateValidation validation,
        IReadOnlyList<ScheduleRegisterRow> rows)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        header.RefreshData();

        int firstColumn = header.FirstColumnNumber;
        int dataStartRow = header.FirstRowNumber + validation.DataStartRowIndex;
        double templateRowHeight = header.GetRowHeight(dataStartRow);

        while ((header.LastRowNumber - dataStartRow + 1) > rows.Count)
        {
            int rowToRemove = header.LastRowNumber;
            if (!header.CanRemoveRow(rowToRemove))
            {
                throw new InvalidOperationException("Revit не разрешил удалить лишнюю пустую строку шаблона.");
            }

            header.RemoveRow(rowToRemove);
        }

        while ((header.LastRowNumber - dataStartRow + 1) < rows.Count)
        {
            int rowToInsert = header.LastRowNumber + 1;
            if (!header.CanInsertRow(rowToInsert))
            {
                throw new InvalidOperationException("Revit не разрешил добавить строку в ведомость спецификаций.");
            }

            header.InsertRow(rowToInsert);
            header.SetRowHeight(rowToInsert, templateRowHeight);
            CopyRowStyles(header, dataStartRow, rowToInsert, firstColumn, firstColumn + 2);
        }

        for (int index = 0; index < rows.Count; index++)
        {
            int row = dataStartRow + index;
            ScheduleRegisterRow item = rows[index];
            header.SetCellText(row, firstColumn, item.SheetNumbers);
            header.SetCellText(row, firstColumn + 1, item.ScheduleTitle);
            header.SetCellText(row, firstColumn + 2, string.Empty);
        }
    }

    private static void CopyRowStyles(
        TableSectionData header,
        int sourceRow,
        int targetRow,
        int firstColumn,
        int lastColumn)
    {
        for (int column = firstColumn; column <= lastColumn; column++)
        {
            if (!header.AllowOverrideCellStyle(targetRow, column))
            {
                continue;
            }

            using TableCellStyle style = header.GetTableCellStyle(sourceRow, column);
            header.SetCellStyle(targetRow, column, style);
        }
    }
}
