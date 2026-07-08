namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed class OpeningViewProfile
{
    public string Name { get; set; } = "Активный план";

    public bool IncludeDoors { get; set; } = true;

    public bool IncludeWindows { get; set; }

    public long? ElevationViewTypeId { get; set; }

    public long? ViewTemplateId { get; set; }

    public int Scale { get; set; } = 50;

    public double CropMarginMm { get; set; } = 600;

    public double DepthMarginMm { get; set; } = 600;

    public string OrientationSource { get; set; } = OpeningViewOrientationSources.ElementFacing;

    public string ViewNameTemplate { get; set; } = "BIM_Opening_{CategoryKey}_{ElementId}_{Family}_{Type}";
}
