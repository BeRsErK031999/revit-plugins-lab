namespace TrueBIM.App.Modules.FinishSchedule.Services;

public enum FinishScheduleLineWeight
{
    Normal,
    Thin
}

public enum FinishScheduleHeaderMergeMode
{
    None,
    CellMerge
}

public sealed record FinishScheduleCellBorderRules(
    FinishScheduleLineWeight Top,
    FinishScheduleLineWeight Bottom,
    FinishScheduleLineWeight Left,
    FinishScheduleLineWeight Right);

public sealed record FinishScheduleHeaderCell(
    int TopRowOffset,
    int LeftColumnIndex,
    int BottomRowOffset,
    int RightColumnIndex,
    string Text)
{
    public FinishScheduleHeaderMergeMode MergeMode =>
        TopRowOffset == BottomRowOffset && LeftColumnIndex == RightColumnIndex
            ? FinishScheduleHeaderMergeMode.None
            : FinishScheduleHeaderMergeMode.CellMerge;
}

public sealed record FinishScheduleHeaderNormalizationPlan(int RowsToInsert);

public static class FinishRoomScheduleStyleRules
{
    public const string LayoutRevision = "v9";
    public const int HeaderRowCount = 4;
    public const string ScheduleTitleText = "Ведомость отделки помещений";
    public const string FinishGroupHeaderText = "Вид отделки элементов интерьера";
    public const string RoomHeaderText = "Наименование или номер помещения";
    public const string NoteHeaderText = "Примечание";
    public const string NormalLineStyleName = "Обычные линии";
    public const string ThinLineStyleName = "Тонкие линии";
    public const double TitleRowHeightMillimeters = 12;
    public const double GroupHeaderRowHeightMillimeters = 8;
    public const double ColumnHeaderRowHeightMillimeters = 12;
    public const double GraphHeaderRowHeightMillimeters = 5;
    public const double TitleTextSizeMillimeters = 3.5;
    public const double ColumnHeaderTextSizeMillimeters = 2.5;
    public const double BodyTextSizeMillimeters = 2.5;
    public const bool ShowBlankLineBetweenGroups = false;

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

    public static int GetHeaderRowsToInsert(int existingRowCount)
    {
        if (existingRowCount < 1 || existingRowCount > HeaderRowCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(existingRowCount),
                existingRowCount,
                $"Header must contain from 1 to {HeaderRowCount} rows before normalization.");
        }

        return HeaderRowCount - existingRowCount;
    }

    public static FinishScheduleHeaderNormalizationPlan BuildHeaderNormalizationPlan(
        int existingRowCount,
        int existingColumnCount,
        int expectedColumnCount)
    {
        if (expectedColumnCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedColumnCount),
                expectedColumnCount,
                "Header must contain at least one expected column.");
        }

        if (existingColumnCount != expectedColumnCount)
        {
            throw new ArgumentException(
                $"Header contains {existingColumnCount} columns instead of {expectedColumnCount}.",
                nameof(existingColumnCount));
        }

        return new FinishScheduleHeaderNormalizationPlan(
            GetHeaderRowsToInsert(existingRowCount));
    }

    public static IReadOnlyList<FinishScheduleHeaderCell> BuildHeaderCells(
        IReadOnlyList<Models.FinishRoomScheduleColumn> columns)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (columns.Count < 3
            || columns[0].Kind != Models.FinishRoomScheduleColumnKind.RoomList
            || columns[columns.Count - 1].Kind != Models.FinishRoomScheduleColumnKind.Note)
        {
            throw new ArgumentException(
                "Header layout requires room-list, finish and note columns.",
                nameof(columns));
        }

        int noteColumn = columns.Count - 1;
        List<FinishScheduleHeaderCell> cells =
        [
            new(0, 0, 1, 0, RoomHeaderText),
            new(0, 1, 0, noteColumn - 1, FinishGroupHeaderText),
            new(0, noteColumn, 1, noteColumn, NoteHeaderText)
        ];
        for (int column = 1; column < noteColumn; column++)
        {
            cells.Add(new FinishScheduleHeaderCell(
                1,
                column,
                1,
                column,
                columns[column].Heading));
        }

        for (int column = 0; column < columns.Count; column++)
        {
            cells.Add(new FinishScheduleHeaderCell(
                2,
                column,
                2,
                column,
                ToGraphLabel(column)));
        }

        return cells;
    }

    public static bool MatchesLineStyleName(string? actualName, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(actualName))
        {
            return false;
        }

        string normalizedActual = actualName!.Trim().Trim('<', '>').Trim();
        string normalizedExpected = expectedName.Trim().Trim('<', '>').Trim();
        return string.Equals(
            normalizedActual,
            normalizedExpected,
            StringComparison.CurrentCultureIgnoreCase);
    }

    private static string ToGraphLabel(int zeroBasedColumn)
    {
        int index = zeroBasedColumn;
        string label = string.Empty;
        do
        {
            label = (char)('A' + index % 26) + label;
            index = index / 26 - 1;
        }
        while (index >= 0);

        return label;
    }
}
