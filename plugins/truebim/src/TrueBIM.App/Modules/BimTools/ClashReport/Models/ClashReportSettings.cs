namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed class ClashReportSettings
{
    public ClashReportProfile Profile { get; set; } = new();

    public Dictionary<string, ClashStateRecord> States { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
