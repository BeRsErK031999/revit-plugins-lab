using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Models;

public sealed record ScheduleSelectionItem(
    ElementId ScheduleId,
    string Name,
    string Context);
