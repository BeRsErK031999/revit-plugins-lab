namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Models;

public sealed record ScheduleColumnVisibilityDecision(
    string FieldName,
    ScheduleColumnVisibilityAction Action,
    string Reason);
