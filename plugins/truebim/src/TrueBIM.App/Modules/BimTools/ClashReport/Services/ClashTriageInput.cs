using TrueBIM.App.Modules.BimTools.ClashReport.Models;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed record ClashTriageInput(
    string Source,
    long ElementId1,
    long ElementId2,
    long? LinkedElementId1,
    long? LinkedElementId2,
    string Element1Category,
    string Element2Category,
    double CenterXFeet,
    double CenterYFeet,
    double CenterZFeet,
    double ApproximateVolumeMm3,
    ClashType ClashType,
    ClashGroupingStrategy GroupingStrategy);
