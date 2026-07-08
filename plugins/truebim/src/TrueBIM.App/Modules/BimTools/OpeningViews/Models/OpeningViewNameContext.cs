namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed record OpeningViewNameContext(
    long ElementId,
    string CategoryKey,
    string CategoryName,
    string Family,
    string Type,
    string Level);
