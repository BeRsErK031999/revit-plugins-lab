namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Models;

public sealed record ScheduleColumnState(
    string FieldName,
    string ColumnHeading,
    bool IsHidden,
    bool CanHide,
    IReadOnlyList<string> CellTexts);
