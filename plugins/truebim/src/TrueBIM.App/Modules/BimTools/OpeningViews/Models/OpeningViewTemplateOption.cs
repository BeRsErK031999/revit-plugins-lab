namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed record OpeningViewTemplateOption(long? ElementId, string DisplayName)
{
    public bool IsNone => ElementId is null;

    public static OpeningViewTemplateOption None { get; } = new(null, "Без шаблона");
}
