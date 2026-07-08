namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed record ClashNavigationResult(
    bool Succeeded,
    string Message,
    string ViewName,
    int SelectedElementCount);
