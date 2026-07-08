namespace TrueBIM.App.Modules.BimTools.AutoTags.Models;

public sealed class AutoTagProfile
{
    public string Name { get; set; } = "Активный вид";

    public bool OnlyUntagged { get; set; } = true;

    public bool UseLeader { get; set; }

    public int MaxPreviewCount { get; set; } = 500;

    public long? SelectedTagTypeId { get; set; }

    public List<long> SelectedCategoryIds { get; set; } = [];
}
