using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewOrientationResolver
{
    public static OpeningViewOrientationResult Resolve(
        FamilyInstance familyInstance,
        ViewPlan activePlan,
        OpeningViewProfile profile)
    {
        XYZ elementFacing = NormalizeHorizontal(familyInstance.FacingOrientation, activePlan.UpDirection);
        if (OpeningViewOrientationSources.NormalizeKey(profile.OrientationSource) != OpeningViewOrientationSources.HostWall)
        {
            return new OpeningViewOrientationResult(elementFacing, OpeningViewOrientationSources.ElementFacing, false);
        }

        if (TryResolveHostWallDirection(familyInstance, elementFacing, out XYZ wallFacing))
        {
            return new OpeningViewOrientationResult(wallFacing, OpeningViewOrientationSources.HostWall, false);
        }

        return new OpeningViewOrientationResult(elementFacing, OpeningViewOrientationSources.ElementFacing, true);
    }

    public static bool TryResolveHostWallDirection(
        FamilyInstance familyInstance,
        XYZ elementFacing,
        out XYZ direction)
    {
        direction = XYZ.Zero;
        if (familyInstance.Host is not Wall wall
            || wall.Location is not LocationCurve locationCurve
            || locationCurve.Curve is not Line wallLine)
        {
            return false;
        }

        XYZ start = wallLine.GetEndPoint(0);
        XYZ end = wallLine.GetEndPoint(1);
        if (!TryResolveWallFacingCoordinates(
            start.X,
            start.Y,
            end.X,
            end.Y,
            elementFacing.X,
            elementFacing.Y,
            out double x,
            out double y))
        {
            return false;
        }

        direction = new XYZ(x, y, 0);
        return true;
    }

    public static bool TryResolveWallFacingCoordinates(
        double startX,
        double startY,
        double endX,
        double endY,
        double elementFacingX,
        double elementFacingY,
        out double x,
        out double y)
    {
        x = 0;
        y = 0;
        double dx = endX - startX;
        double dy = endY - startY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            return false;
        }

        double candidateX = -dy / length;
        double candidateY = dx / length;
        double facingLength = Math.Sqrt(elementFacingX * elementFacingX + elementFacingY * elementFacingY);
        if (facingLength >= 1e-6)
        {
            double dot = candidateX * elementFacingX / facingLength + candidateY * elementFacingY / facingLength;
            if (dot < 0)
            {
                candidateX = -candidateX;
                candidateY = -candidateY;
            }
        }

        x = candidateX;
        y = candidateY;
        return true;
    }

    private static XYZ NormalizeHorizontal(XYZ value, XYZ fallback)
    {
        XYZ horizontal = new(value.X, value.Y, 0);
        if (horizontal.GetLength() < 1e-6)
        {
            horizontal = new(fallback.X, fallback.Y, 0);
        }

        return horizontal.GetLength() < 1e-6
            ? XYZ.BasisY
            : horizontal.Normalize();
    }
}
