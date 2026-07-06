namespace TrueBIM.App.Modules.BimTools.JoinCut.Models;

public sealed class JoinCutConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Новая конфигурация";

    public bool IsDefault { get; set; }

    public List<JoinRule> JoinRules { get; set; } = [];

    public List<CutRule> CutRules { get; set; } = [];

    public JoinCutTab LastSelectedTab { get; set; } = JoinCutTab.Join;

    public ProcessingScope LastSelectedScope { get; set; } = ProcessingScope.SelectedElements;

    public JoinAction LastSelectedJoinAction { get; set; } = JoinAction.Join;

    public CutAction LastSelectedCutAction { get; set; } = CutAction.Cut;
}
