namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleTarget(
    long Id,
    string Name,
    bool IsActive)
{
    public string DisplayName => IsActive ? $"{Name} (активная)" : Name;
}
