using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldControlPointSnapService
{
    public const double SnapRadiusMillimeters = 1000;
    private const double MillimetersPerFoot = 304.8;
    private const double GeometryToleranceFeet = 1e-7;
    private const double AutomaticCornerToleranceFeet = 1e-5;
    private const double MinimumAutomaticSpanFeet = 0.01;

    public IsoFieldControlPointTriad? CreateAutomaticOuterCornerTriad(
        IsoFieldHostGeometry hostGeometry)
    {
        if (hostGeometry is null)
        {
            throw new ArgumentNullException(nameof(hostGeometry));
        }

        IReadOnlyList<IsoFieldPoint>? outerBoundary = FindOuterBoundary(hostGeometry);
        if (outerBoundary is null)
        {
            return null;
        }

        IsoFieldPoint[] corners = GetDistinctCorners(outerBoundary).ToArray();
        double minimumX = corners.Min(point => point.X);
        double maximumX = corners.Max(point => point.X);
        double minimumY = corners.Min(point => point.Y);
        double maximumY = corners.Max(point => point.Y);
        if (maximumX - minimumX < MinimumAutomaticSpanFeet
            || maximumY - minimumY < MinimumAutomaticSpanFeet)
        {
            return null;
        }

        IsoFieldPoint[] targets =
        [
            new IsoFieldPoint(minimumX, maximumY),
            new IsoFieldPoint(maximumX, maximumY),
            new IsoFieldPoint(minimumX, minimumY),
            new IsoFieldPoint(maximumX, minimumY)
        ];
        IsoFieldPoint?[] resolvedCorners = targets
            .Select(target => FindMatchingCorner(corners, target))
            .ToArray();
        if (resolvedCorners.Any(point => point is null))
        {
            return null;
        }

        return new IsoFieldControlPointTriad(
            resolvedCorners[0]!,
            resolvedCorners[1]!,
            resolvedCorners[2]!);
    }

    public IsoFieldPoint SnapToNearestOuterCorner(
        IsoFieldPoint selectedPoint,
        IsoFieldHostGeometry hostGeometry)
    {
        if (hostGeometry is null)
        {
            throw new ArgumentNullException(nameof(hostGeometry));
        }

        IReadOnlyList<IsoFieldPoint>? outerBoundary = FindOuterBoundary(hostGeometry);
        if (outerBoundary is null)
        {
            throw new InvalidOperationException("Не удалось найти углы внешнего контура конструкции.");
        }

        IsoFieldPoint nearestCorner = GetDistinctCorners(outerBoundary)
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

    private static IReadOnlyList<IsoFieldPoint>? FindOuterBoundary(
        IsoFieldHostGeometry hostGeometry)
    {
        return hostGeometry.BoundaryLoopsFeet
            .Where(loop => loop.Count >= 3)
            .OrderByDescending(loop => Math.Abs(CalculateSignedArea(loop)))
            .FirstOrDefault();
    }

    private static IEnumerable<IsoFieldPoint> GetDistinctCorners(
        IReadOnlyList<IsoFieldPoint> boundary)
    {
        return boundary.Count > 1
            && Distance(boundary[0], boundary[boundary.Count - 1]) <= GeometryToleranceFeet
                ? boundary.Take(boundary.Count - 1)
                : boundary;
    }

    private static IsoFieldPoint? FindMatchingCorner(
        IReadOnlyList<IsoFieldPoint> corners,
        IsoFieldPoint target)
    {
        IsoFieldPoint nearestCorner = corners
            .OrderBy(corner => Distance(corner, target))
            .First();
        return Distance(nearestCorner, target) <= AutomaticCornerToleranceFeet
            ? nearestCorner
            : null;
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
