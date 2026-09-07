namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;

public static class ScheduleRegisterConstants
{
    public const string TemplateScheduleName = "• Т • Общие данные • Ведомость спецификаций";
    public const string ScheduleTitle = "Ведомость спецификаций";
    public const string SheetColumnTitle = "Лист";
    public const string NameColumnTitle = "Наименование";
    public const string NoteColumnTitle = "Примечание";
    public const string DefaultExcludedValue = "Не специфицировать";
}

public sealed class ScheduleRegisterSettings
{
    public bool FilterEnabled { get; set; }

    public string FilterParameterName { get; set; } = string.Empty;

    public string ExcludedValue { get; set; } = ScheduleRegisterConstants.DefaultExcludedValue;

    public string TemplateProjectPath { get; set; } = string.Empty;
}

public sealed record ScheduleRegisterTemplateSnapshot(
    IReadOnlyList<IReadOnlyList<string>> HeaderRows);

public sealed record ScheduleRegisterTemplateValidation(
    bool IsValid,
    bool HasValidStructure,
    bool HasFilledDataRows,
    int TitleRowIndex,
    int ColumnHeaderRowIndex,
    int DataStartRowIndex,
    int DataRowCount,
    IReadOnlyList<string> Issues)
{
    public static ScheduleRegisterTemplateValidation Missing(string issue)
    {
        return new ScheduleRegisterTemplateValidation(
            false,
            false,
            false,
            -1,
            -1,
            -1,
            0,
            [issue]);
    }
}

public sealed record SchedulePlacementSnapshot(
    long ScheduleId,
    string ScheduleBrowserName,
    string ScheduleTitle,
    long SheetId,
    string SheetNumber,
    int SegmentIndex);

public sealed record ScheduleRegisterRow(
    long ScheduleId,
    string SheetNumbers,
    string ScheduleTitle,
    string ScheduleBrowserName);

public sealed record ScheduleRegisterAggregationResult(
    IReadOnlyList<ScheduleRegisterRow> Rows,
    IReadOnlyList<string> Warnings);

public sealed record ScheduleRegisterCollectionResult(
    IReadOnlyList<SchedulePlacementSnapshot> Placements,
    int ExcludedByFilterCount,
    IReadOnlyList<string> Warnings);

public sealed record ScheduleRegisterTemplateInspection(
    long? ScheduleId,
    ScheduleRegisterTemplateValidation Validation,
    int PlacementCount)
{
    public bool Exists => ScheduleId.HasValue;

    public bool IsValid => Exists && Validation.IsValid && PlacementCount == 0;

    public bool CanRepairLocally => Exists
                                    && Validation.HasValidStructure
                                    && !IsValid;
}

public sealed record ScheduleRegisterCreationResult(
    long ScheduleId,
    string ScheduleName,
    int RowCount,
    IReadOnlyList<string> Warnings);

public sealed class ScheduleRegisterSheetOption
{
    public ScheduleRegisterSheetOption(
        long sheetId,
        string sheetNumber,
        string sheetName,
        int placedScheduleCount)
    {
        SheetId = sheetId;
        SheetNumber = sheetNumber;
        SheetName = sheetName;
        PlacedScheduleCount = placedScheduleCount;
    }

    public long SheetId { get; }

    public string SheetNumber { get; }

    public string SheetName { get; }

    public int PlacedScheduleCount { get; }

    public string PlacedSchedulesText => PlacedScheduleCount == 0
        ? "Нет"
        : PlacedScheduleCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
}
