namespace TrueBIM.App.Modules.FinishSchedule.Services;

public enum FinishScheduleLineWeight
{
    Normal,
    Thin
}

public sealed record FinishScheduleCellBorderRules(
    FinishScheduleLineWeight Top,
    FinishScheduleLineWeight Bottom,
    FinishScheduleLineWeight Left,
    FinishScheduleLineWeight Right);

public static class FinishRoomScheduleStyleRules
{
    public const string LayoutRevision = "v3";
    public const double TitleRowHeightMillimeters = 12;
    public const double ColumnHeaderRowHeightMillimeters = 8;
    public const double TitleTextSizeMillimeters = 3.5;
    public const double ColumnHeaderTextSizeMillimeters = 2.5;

    public static FinishScheduleCellBorderRules HeaderBorders { get; } = new(
        FinishScheduleLineWeight.Normal,
        FinishScheduleLineWeight.Normal,
        FinishScheduleLineWeight.Normal,
        FinishScheduleLineWeight.Normal);

    public static FinishScheduleCellBorderRules BodyBorders(bool isFirstRow, bool isLastRow)
    {
        return new FinishScheduleCellBorderRules(
            isFirstRow ? FinishScheduleLineWeight.Normal : FinishScheduleLineWeight.Thin,
            isLastRow ? FinishScheduleLineWeight.Normal : FinishScheduleLineWeight.Thin,
            FinishScheduleLineWeight.Normal,
            FinishScheduleLineWeight.Normal);
    }
}
