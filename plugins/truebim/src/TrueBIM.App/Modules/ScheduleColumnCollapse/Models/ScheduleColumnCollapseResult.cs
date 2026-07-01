using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Models;

public sealed record ScheduleColumnCollapseResult(
    bool Succeeded,
    string Message,
    ElementId? ScheduleId,
    string? ScheduleName,
    int HiddenColumnCount,
    int VisibleColumnCount,
    int UnchangedColumnCount)
{
    public static ScheduleColumnCollapseResult Failure(string message)
    {
        Guard.NotNullOrWhiteSpace(message, nameof(message));

        return new ScheduleColumnCollapseResult(
            Succeeded: false,
            message,
            ScheduleId: null,
            ScheduleName: null,
            HiddenColumnCount: 0,
            VisibleColumnCount: 0,
            UnchangedColumnCount: 0);
    }
}
