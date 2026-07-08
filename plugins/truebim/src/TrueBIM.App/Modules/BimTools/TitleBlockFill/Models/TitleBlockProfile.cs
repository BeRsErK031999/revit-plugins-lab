namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

public sealed class TitleBlockProfile
{
    public string Name { get; set; } = "Рабочая документация";

    public List<TitleBlockParameterRule> Rules { get; set; } = new();
}
