namespace TrueBIM.App.Modules.BimTools.JoinCut.Models;

public sealed class CutRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Новое правило вырезания";

    public ElementFilterDefinition CuttingElementsFilter { get; set; } = new();

    public ElementFilterDefinition CutElementsFilter { get; set; } = new();

    public bool SplitFacesOfCuttingSolid { get; set; }

    public bool Enabled { get; set; } = true;
}
