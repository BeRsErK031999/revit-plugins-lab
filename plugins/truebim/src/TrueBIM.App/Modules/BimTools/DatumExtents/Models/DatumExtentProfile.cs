namespace TrueBIM.App.Modules.BimTools.DatumExtents.Models;

public sealed class DatumExtentProfile
{
    public string Name { get; set; } = "Активный вид";

    public string TargetExtentType { get; set; } = DatumExtentTargets.ViewSpecific;

    public bool IncludeEnd0 { get; set; } = true;

    public bool IncludeEnd1 { get; set; } = true;

    public bool IncludeGrids { get; set; } = true;

    public bool IncludeLevels { get; set; } = true;
}
