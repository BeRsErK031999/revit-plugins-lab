namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed class ClashScanOptions
{
    public bool ScanCurrentModel { get; set; } = true;

    public bool ScanRvtLinks { get; set; } = true;

    public bool ScanLinksAgainstEachOther { get; set; }

    public double MinimumOverlapMm { get; set; }
}
