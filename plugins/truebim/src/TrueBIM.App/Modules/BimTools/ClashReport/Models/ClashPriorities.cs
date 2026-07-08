namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public static class ClashPriorities
{
    public static string ToDisplayName(ClashPriority priority)
    {
        return priority switch
        {
            ClashPriority.Low => "Low",
            ClashPriority.Medium => "Medium",
            ClashPriority.High => "High",
            ClashPriority.Critical => "Critical",
            _ => "Low"
        };
    }
}
