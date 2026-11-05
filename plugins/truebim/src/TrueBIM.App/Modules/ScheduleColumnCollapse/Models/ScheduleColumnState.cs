namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Models;

/// <param name="CellTexts">Body values only, without schedule title or column headers.</param>
public sealed record ScheduleColumnState(
    string FieldName,
    string ColumnHeading,
    bool IsHidden,
    bool CanHide,
    IReadOnlyList<string> CellTexts,
    bool IsNumeric = false,
    IReadOnlyList<double?>? ParsedNumericValues = null);
