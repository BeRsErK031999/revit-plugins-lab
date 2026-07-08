namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed class ClashReportProfile
{
    public string Name { get; set; } = "Координационная проверка";

    public string LastCsvPath { get; set; } = string.Empty;

    public double SectionBoxPaddingMm { get; set; } = 1500;

    public double MinimumOverlapMm { get; set; }

    public bool HighlightOnNavigate { get; set; } = true;

    public bool ScanCurrentModel { get; set; } = true;

    public bool ScanRvtLinks { get; set; } = true;

    public bool ScanLinksAgainstEachOther { get; set; }
}
