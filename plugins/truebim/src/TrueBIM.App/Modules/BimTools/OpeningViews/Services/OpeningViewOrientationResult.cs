using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed record OpeningViewOrientationResult(XYZ Direction, string Source, bool UsedFallback);
