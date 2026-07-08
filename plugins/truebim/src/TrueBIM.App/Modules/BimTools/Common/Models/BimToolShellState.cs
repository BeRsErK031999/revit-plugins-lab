namespace TrueBIM.App.Modules.BimTools.Common.Models;

public sealed class BimToolShellState
{
    public string ToolTitle { get; set; } = string.Empty;

    public string? DocumentTitle { get; set; }

    public DateTimeOffset LastOpenedAtUtc { get; set; }

    public DateTimeOffset? LastPreviewAtUtc { get; set; }

    public DateTimeOffset? LastExecuteAtUtc { get; set; }

    public int PreviewRequestCount { get; set; }

    public int ExecuteRequestCount { get; set; }
}
