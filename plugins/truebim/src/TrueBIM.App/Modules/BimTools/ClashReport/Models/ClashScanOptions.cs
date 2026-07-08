namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed class ClashScanOptions
{
    public bool ScanCurrentModel { get; set; } = true;

    public bool ScanRvtLinks { get; set; } = true;

    public bool ScanLinksAgainstEachOther { get; set; }

    public double MinimumOverlapMm { get; set; }

    public ClashType ClashType { get; set; } = ClashType.Hard;

    public ClashGroupingStrategy GroupingStrategy { get; set; } = ClashGroupingStrategy.SourceCategoryPair;
}
