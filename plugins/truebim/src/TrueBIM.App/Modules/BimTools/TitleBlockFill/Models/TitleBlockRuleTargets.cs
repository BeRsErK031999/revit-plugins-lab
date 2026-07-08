namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

public static class TitleBlockRuleTargets
{
    public const string Sheet = "Лист";

    public const string TitleBlock = "Штамп";

    public static IReadOnlyList<string> All { get; } =
    [
        Sheet,
        TitleBlock
    ];
}
