namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleCategoryOption(long CategoryId, string Name)
{
    public string DisplayName => Name;
}
