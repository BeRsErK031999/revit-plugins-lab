namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed class ClashImportResult
{
    public ClashImportResult(IReadOnlyList<ClashItem> items, IReadOnlyList<string> errors)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    public IReadOnlyList<ClashItem> Items { get; }

    public IReadOnlyList<string> Errors { get; }
}
