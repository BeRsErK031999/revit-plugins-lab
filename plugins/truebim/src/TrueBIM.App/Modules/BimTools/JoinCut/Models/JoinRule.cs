namespace TrueBIM.App.Modules.BimTools.JoinCut.Models;

public sealed class JoinRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Новое правило соединения";

    public ElementFilterDefinition LeftFilter { get; set; } = new();

    public ElementFilterDefinition RightFilter { get; set; } = new();

    public bool OnlyParallelWalls { get; set; }

    public bool Enabled { get; set; } = true;
}
