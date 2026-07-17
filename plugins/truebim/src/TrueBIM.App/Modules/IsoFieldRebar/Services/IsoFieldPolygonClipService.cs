using System.Windows;
using System.Windows.Media;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldPolygonClipService
{
    private const double GeometryToleranceFeet = 1e-7;

    public IsoFieldClippedZone Clip(
        IsoFieldPolyline mappedZone,
        IReadOnlyList<IsoFieldPoint> outerBoundaryFeet,
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> holeBoundariesFeet)
    {
        if (mappedZone is null)
        {
            throw new ArgumentNullException(nameof(mappedZone));
        }

        IReadOnlyList<IsoFieldPoint> normalizedZone = NormalizeLoop(mappedZone.Points, "Зона");
        IReadOnlyList<IsoFieldPoint> normalizedOuter = NormalizeLoop(outerBoundaryFeet, "Внешний контур плиты");
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> normalizedHoles = holeBoundariesFeet
            .Select((hole, index) => NormalizeLoop(hole, $"Отверстие плиты {index + 1}"))
            .ToArray();

        PathGeometry zoneGeometry = CreateGeometry([normalizedZone]);
        PathGeometry hostGeometry = CreateGeometry([normalizedOuter, .. normalizedHoles]);
        PathGeometry intersection = Geometry.Combine(
            zoneGeometry,
            hostGeometry,
            GeometryCombineMode.Intersect,
            transform: null,
            GeometryToleranceFeet,
            ToleranceType.Absolute);
        PathGeometry flattened = intersection.GetFlattenedPathGeometry(
            GeometryToleranceFeet,
            ToleranceType.Absolute);
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> resultLoops = ExtractLoops(flattened);
        IReadOnlyList<IsoFieldPolygonRegion> regions = BuildRegions(resultLoops);
        double originalArea = Math.Abs(CalculateSignedArea(normalizedZone));
        double clippedArea = regions.Sum(region => region.AreaSquareFeet);

        return new IsoFieldClippedZone(
            mappedZone.Id,
            mappedZone.ZoneName,
            mappedZone.Confidence,
            mappedZone.LayerRole,
            mappedZone.LegendBandIndex,
            regions,
            originalArea,
            clippedArea);
    }

    public IReadOnlyList<IsoFieldPolygonRegion> UnionRegions(
        IReadOnlyList<IsoFieldPolygonRegion> regions)
    {
        if (regions is null)
        {
            throw new ArgumentNullException(nameof(regions));
        }

        if (regions.Count == 0)
        {
            return Array.Empty<IsoFieldPolygonRegion>();
        }

        PathGeometry combined = CreateGeometry(ToLoops(regions[0]));
        foreach (IsoFieldPolygonRegion region in regions.Skip(1))
        {
            combined = Geometry.Combine(
                combined,
                CreateGeometry(ToLoops(region)),
                GeometryCombineMode.Union,
                transform: null,
                GeometryToleranceFeet,
                ToleranceType.Absolute);
        }

        PathGeometry flattened = combined.GetFlattenedPathGeometry(
            GeometryToleranceFeet,
            ToleranceType.Absolute);
        return BuildRegions(ExtractLoops(flattened));
    }

    private static IReadOnlyList<IReadOnlyList<IsoFieldPoint>> ToLoops(
        IsoFieldPolygonRegion region)
    {
        return [region.OuterBoundaryFeet, .. region.HoleBoundariesFeet];
    }

    private static PathGeometry CreateGeometry(
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> loops)
    {
        PathGeometry geometry = new()
        {
            FillRule = FillRule.EvenOdd
        };
        foreach (IReadOnlyList<IsoFieldPoint> loop in loops)
        {
            PathFigure figure = new()
            {
                StartPoint = ToPoint(loop[0]),
                IsClosed = true,
                IsFilled = true
            };
            figure.Segments.Add(new PolyLineSegment(
                new PointCollection(loop.Skip(1).Select(ToPoint)),
                isStroked: true));
            geometry.Figures.Add(figure);
        }

        return geometry;
    }

    private static IReadOnlyList<IReadOnlyList<IsoFieldPoint>> ExtractLoops(
        PathGeometry geometry)
    {
        List<IReadOnlyList<IsoFieldPoint>> loops = new();
        foreach (PathFigure figure in geometry.Figures)
        {
            List<IsoFieldPoint> points = [ToIsoFieldPoint(figure.StartPoint)];
            foreach (PathSegment segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        points.Add(ToIsoFieldPoint(line.Point));
                        break;
                    case PolyLineSegment polyline:
                        points.AddRange(polyline.Points.Select(ToIsoFieldPoint));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"После flattening получен неподдерживаемый сегмент {segment.GetType().Name}.");
                }
            }

            IReadOnlyList<IsoFieldPoint> normalized = NormalizeOutputLoop(points);
            if (normalized.Count >= 4
                && Math.Abs(CalculateSignedArea(normalized)) > GeometryToleranceFeet * GeometryToleranceFeet)
            {
                loops.Add(normalized);
            }
        }

        return loops;
    }

    private static IReadOnlyList<IsoFieldPolygonRegion> BuildRegions(
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> loops)
    {
        if (loops.Count == 0)
        {
            return Array.Empty<IsoFieldPolygonRegion>();
        }

        int[] depths = loops
            .Select((loop, index) => loops
                .Where((_, otherIndex) => otherIndex != index)
                .Count(other => IsInsidePolygon(loop[0], other)))
            .ToArray();
        List<int> outerIndexes = Enumerable.Range(0, loops.Count)
            .Where(index => depths[index] % 2 == 0)
            .ToList();
        List<IsoFieldPolygonRegion> regions = new();
        foreach (int outerIndex in outerIndexes)
        {
            IReadOnlyList<IsoFieldPoint> outer = loops[outerIndex];
            IReadOnlyList<IReadOnlyList<IsoFieldPoint>> holes = Enumerable.Range(0, loops.Count)
                .Where(index => depths[index] == depths[outerIndex] + 1
                    && IsInsidePolygon(loops[index][0], outer)
                    && !outerIndexes.Any(candidate => candidate != outerIndex
                        && depths[candidate] == depths[outerIndex]
                        && IsInsidePolygon(loops[index][0], loops[candidate])
                        && Math.Abs(CalculateSignedArea(loops[candidate]))
                            < Math.Abs(CalculateSignedArea(outer))))
                .Select(index => loops[index])
                .ToArray();
            double area = Math.Abs(CalculateSignedArea(outer))
                - holes.Sum(hole => Math.Abs(CalculateSignedArea(hole)));
            if (area > GeometryToleranceFeet * GeometryToleranceFeet)
            {
                regions.Add(new IsoFieldPolygonRegion(outer, holes, area));
            }
        }

        return regions
            .OrderByDescending(region => region.AreaSquareFeet)
            .ToArray();
    }

    public IReadOnlyList<IsoFieldPolygonRegion> IntersectRegions(
        IReadOnlyList<IsoFieldPolygonRegion> first,
        IReadOnlyList<IsoFieldPolygonRegion> second)
    {
        if (first is null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (second is null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        if (first.Count == 0 || second.Count == 0)
        {
            return Array.Empty<IsoFieldPolygonRegion>();
        }

        PathGeometry intersection = Geometry.Combine(
            CreateGeometry(first.SelectMany(ToLoops).ToArray()),
            CreateGeometry(second.SelectMany(ToLoops).ToArray()),
            GeometryCombineMode.Intersect,
            transform: null,
            GeometryToleranceFeet,
            ToleranceType.Absolute);
        PathGeometry flattened = intersection.GetFlattenedPathGeometry(
            GeometryToleranceFeet,
            ToleranceType.Absolute);
        return BuildRegions(ExtractLoops(flattened));
    }

    private static IReadOnlyList<IsoFieldPoint> NormalizeLoop(
        IReadOnlyList<IsoFieldPoint> points,
        string label)
    {
        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        IReadOnlyList<IsoFieldPoint> normalized = NormalizeOutputLoop(points);
        if (normalized.Count < 4)
        {
            throw new InvalidOperationException($"{label} должна содержать минимум три различные точки.");
        }

        if (Math.Abs(CalculateSignedArea(normalized)) <= GeometryToleranceFeet * GeometryToleranceFeet)
        {
            throw new InvalidOperationException($"{label} имеет нулевую площадь.");
        }

        return normalized;
    }

    private static IReadOnlyList<IsoFieldPoint> NormalizeOutputLoop(
        IReadOnlyList<IsoFieldPoint> points)
    {
        List<IsoFieldPoint> normalized = new();
        foreach (IsoFieldPoint point in points)
        {
            if (point is null || !IsFinite(point.X) || !IsFinite(point.Y))
            {
                continue;
            }

            if (normalized.Count == 0
                || Distance(normalized[normalized.Count - 1], point) > GeometryToleranceFeet)
            {
                normalized.Add(point);
            }
        }

        if (normalized.Count > 1
            && Distance(normalized[0], normalized[normalized.Count - 1]) <= GeometryToleranceFeet)
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        if (normalized.Count >= 3)
        {
            normalized.Add(normalized[0]);
        }

        return normalized;
    }

    private static bool IsInsidePolygon(
        IsoFieldPoint point,
        IReadOnlyList<IsoFieldPoint> polygon)
    {
        bool inside = false;
        int lastIndex = polygon.Count - 1;
        for (int index = 0; index < polygon.Count; index++)
        {
            IsoFieldPoint start = polygon[lastIndex];
            IsoFieldPoint end = polygon[index];
            bool crosses = (end.Y > point.Y) != (start.Y > point.Y)
                && point.X < ((start.X - end.X) * (point.Y - end.Y) / (start.Y - end.Y)) + end.X;
            if (crosses)
            {
                inside = !inside;
            }

            lastIndex = index;
        }

        return inside;
    }

    private static double CalculateSignedArea(IReadOnlyList<IsoFieldPoint> loop)
    {
        double area = 0;
        for (int index = 0; index < loop.Count - 1; index++)
        {
            area += (loop[index].X * loop[index + 1].Y)
                - (loop[index + 1].X * loop[index].Y);
        }

        return area / 2;
    }

    private static double Distance(IsoFieldPoint first, IsoFieldPoint second)
    {
        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static Point ToPoint(IsoFieldPoint point)
    {
        return new Point(point.X, point.Y);
    }

    private static IsoFieldPoint ToIsoFieldPoint(Point point)
    {
        return new IsoFieldPoint(point.X, point.Y);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
