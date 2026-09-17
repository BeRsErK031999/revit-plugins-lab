using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public static class IsoFieldArrayFamilyCoordinates
{
    public static IsoFieldRebarPoint3D GetInsertionPoint(IsoFieldArrayRebarPlacement placement)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }

        // The supplied 000 family uses the centre of its zone as insertion origin.
        // FirstBarEnd and LastBarStart are opposite corners of that rectangle.
        return new IsoFieldRebarPoint3D(
            (placement.FirstBarEnd.XFeet + placement.LastBarStart.XFeet) / 2,
            (placement.FirstBarEnd.YFeet + placement.LastBarStart.YFeet) / 2,
            (placement.FirstBarEnd.ZFeet + placement.LastBarStart.ZFeet) / 2);
    }
}
