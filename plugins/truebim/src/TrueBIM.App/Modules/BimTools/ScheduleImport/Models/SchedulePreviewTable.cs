namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record SchedulePreviewTable(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public static SchedulePreviewTable Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<IReadOnlyList<string>>());
}
