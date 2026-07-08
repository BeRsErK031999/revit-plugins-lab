namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public static class ClashTypes
{
    public static string ToDisplayName(ClashType type)
    {
        return type switch
        {
            ClashType.Hard => "Hard",
            ClashType.Clearance => "Clearance",
            ClashType.Semantic => "Semantic",
            _ => "Hard"
        };
    }
}
