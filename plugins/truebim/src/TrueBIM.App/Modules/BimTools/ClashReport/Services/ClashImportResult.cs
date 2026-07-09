using TrueBIM.App.Modules.BimTools.ClashReport.Models;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed record ClashImportResult(
    IReadOnlyList<ClashItem> Items,
    IReadOnlyList<string> Messages);
