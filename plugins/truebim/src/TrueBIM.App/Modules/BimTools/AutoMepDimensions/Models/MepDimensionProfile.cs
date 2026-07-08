namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;

public sealed class MepDimensionProfile
{
    public string Name { get; set; } = "Активный план";

    public bool IncludePipes { get; set; } = true;

    public bool IncludeDucts { get; set; }

    public bool IncludeCableTrays { get; set; }

    public bool IncludeConduits { get; set; }

    public bool AllowElementReferenceFallback { get; set; } = true;

    public double AngleToleranceDegrees { get; set; } = 10;

    public string DimensionLinePlacement { get; set; } = MepDimensionLinePlacements.Center;

    public double DimensionOffsetMm { get; set; } = 500;
}
