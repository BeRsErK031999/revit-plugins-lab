using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldControlPointSnapService
{
    public const double SnapRadiusMillimeters = 1000;
    private const double MillimetersPerFoot = 304.8;
    private const double GeometryToleranceFeet = 1e-7;

    public IsoFieldPoint SnapToNearestOuterCorner(
        IsoFieldPoint selectedPoint,
        IsoFieldHostGeometry hostGeometry)
    {
        if (hostGeometry is null)
        {
            throw new ArgumentNullException(nameof(hostGeometry));
        }

        IReadOnlyList<IsoFieldPoint>? outerBoundary = hostGeometry.BoundaryLoopsFeet
            .Where(loop => loop.Count >= 3)
            .OrderByDescending(loop => Math.Abs(CalculateSignedArea(loop)))
            .FirstOrDefault();
        if (outerBoundary is null)
        {
            throw new InvalidOperationException("Не удалось найти углы внешнего контура конструкции.");
        }

        IEnumerable<IsoFieldPoint> corners = outerBoundary;
        if (outerBoundary.Count > 1
            && Distance(outerBoundary[0], outerBoundary[outerBoundary.Count - 1]) <= GeometryToleranceFeet)
        {
            corners = outerBoundary.Take(outerBoundary.Count - 1);
        }

        IsoFieldPoint nearestCorner = corners
            .OrderBy(corner => Distance(corner, selectedPoint))
            .First();
        double distanceMillimeters = Distance(nearestCorner, selectedPoint) * MillimetersPerFoot;
        if (distanceMillimeters > SnapRadiusMillimeters)
        {
            throw new InvalidOperationException(
                $"Щёлкните ближе к углу внешнего контура конструкции — не дальше {SnapRadiusMillimeters:0} мм. Точка не сохранена.");
        }

        return nearestCorner;
    }

    private static double CalculateSignedArea(IReadOnlyList<IsoFieldPoint> loop)
    {
        double area = 0;
        for (int index = 0; index < loop.Count - 1; index++)
        {
            area += (loop[index].X * loop[index + 1].Y)
                - (loop[index + 1].X * loop[index].Y);
        }

        if (loop.Count > 2
            && Distance(loop[0], loop[loop.Count - 1]) > GeometryToleranceFeet)
        {
            area += (loop[loop.Count - 1].X * loop[0].Y)
                - (loop[0].X * loop[loop.Count - 1].Y);
        }

        return area / 2;
    }

    private static double Distance(IsoFieldPoint first, IsoFieldPoint second)
    {
        double deltaX = first.X - second.X;
        double deltaY = first.Y - second.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}
