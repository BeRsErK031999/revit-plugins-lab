using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Models;

public sealed record ScheduleColumnCollapseResult(
    bool Succeeded,
    string Message,
    ElementId? CollapsedScheduleId,
    string? SourceScheduleName,
    string? CollapsedScheduleName,
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
            CollapsedScheduleId: null,
            SourceScheduleName: null,
            CollapsedScheduleName: null,
            HiddenColumnCount: 0,
            VisibleColumnCount: 0,
            UnchangedColumnCount: 0);
    }
}
