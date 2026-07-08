namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed class ClashStateRecord
{
    public ClashStatus Status { get; set; } = ClashStatus.Open;

    public string Comment { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
