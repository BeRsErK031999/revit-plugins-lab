namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ParsedRow(
    int RowIndex,
    IReadOnlyList<string> Values);
