namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public static class ClashGroupingStrategies
{
    public static string ToDisplayName(ClashGroupingStrategy strategy)
    {
        return strategy switch
        {
            ClashGroupingStrategy.SourceCategoryPair => "Source/category",
            ClashGroupingStrategy.ElementPair => "Element pair",
            ClashGroupingStrategy.LocationBucket => "Location bucket",
            _ => "Source/category"
        };
    }
}
