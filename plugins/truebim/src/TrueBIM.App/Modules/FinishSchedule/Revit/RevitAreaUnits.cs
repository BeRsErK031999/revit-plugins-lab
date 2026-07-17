using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal static class RevitAreaUnits
{
    public static double ToSquareMeters(double internalArea)
    {
#if REVIT2021_OR_GREATER
        return UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);
#else
        return UnitUtils.ConvertFromInternalUnits(internalArea, DisplayUnitType.DUT_SQUARE_METERS);
#endif
    }
}
