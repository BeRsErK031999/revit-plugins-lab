using System.Globalization;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSlabBindingService
{
    private const double MinimumImageSpanPixels = 1;
    private const double MinimumHostSpanFeet = 0.01;
    private const double BoundaryToleranceFeet = 1e-6;
    private const int SegmentSampleCount = 8;

    public IsoFieldSlabBindingAnalysis Analyze(
        IsoFieldRecognitionResult recognitionResult,
        IsoFieldHostGeometry hostGeometry,
        IsoFieldSlabBindingInput input)
    {
        if (recognitionResult is null)
        {
            throw new ArgumentNullException(nameof(recognitionResult));
        }

        ValidateHostGeometry(hostGeometry);
        IsoFieldPlanarTransform transform = BuildTransform(input);
        IReadOnlyList<IsoFieldPoint> outerBoundary = hostGeometry.BoundaryLoopsFeet
            .OrderByDescending(loop => Math.Abs(CalculateSignedArea(loop)))
            .First();
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> holes = hostGeometry.BoundaryLoopsFeet
            .Where(loop => !ReferenceEquals(loop, outerBoundary))
            .ToArray();
        IsoFieldPolyline[] mappedZones = recognitionResult.Polylines
            .Select(polyline => polyline with
            {
                Points = polyline.Points.Select(transform.Map).ToArray()
            })
            .ToArray();

        int totalSamples = 0;
        int insideSamples = 0;
        List<string> outsideZoneIds = new();
        foreach (IsoFieldPolyline zone in mappedZones)
        {
            IsoFieldPoint[] samples = BuildSamples(zone.Points).ToArray();
            int zoneInsideCount = samples.Count(point => IsInsideHost(point, outerBoundary, holes));
            bool overlapsHole = holes.Any(hole => PolygonsOverlap(zone.Points, hole));
            totalSamples += samples.Length;
            insideSamples += zoneInsideCount;
            if (samples.Length == 0 || zoneInsideCount != samples.Length || overlapsHole)
            {
                outsideZoneIds.Add(zone.Id);
            }
        }

        int outsideZoneCount = outsideZoneIds.Count;
        bool controlPointsInside = IsInsideHost(input.HostPoint1Feet, outerBoundary, holes)
            && IsInsideHost(input.HostPoint2Feet, outerBoundary, holes);
        double insideRatio = totalSamples == 0 ? 0 : (double)insideSamples / totalSamples;
        bool canProceed = recognitionResult.Polylines.Count > 0
            && outsideZoneCount == 0
            && controlPointsInside;
        List<string> diagnostics = BuildDiagnostics(
            transform,
            recognitionResult.Polylines.Count,
            outsideZoneCount,
            insideRatio,
            controlPointsInside,
            holes.Count,
            canProceed);

        return new IsoFieldSlabBindingAnalysis(
            transform,
            hostGeometry,
            mappedZones,
            outerBoundary,
            holes,
            [input.HostPoint1Feet, input.HostPoint2Feet],
            outsideZoneIds,
            outsideZoneCount,
            insideRatio,
            diagnostics,
            canProceed);
    }

    public IsoFieldPlanarTransform BuildTransform(IsoFieldSlabBindingInput input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        ValidatePoint(input.ImagePoint1, "Первая точка изображения");
        ValidatePoint(input.ImagePoint2, "Вторая точка изображения");
        ValidatePoint(input.HostPoint1Feet, "Первая точка плиты");
        ValidatePoint(input.HostPoint2Feet, "Вторая точка плиты");

        double imageDeltaX = input.ImagePoint2.X - input.ImagePoint1.X;
        double imageDeltaY = input.ImagePoint2.Y - input.ImagePoint1.Y;
        if (input.MirrorImageY)
        {
            imageDeltaY = -imageDeltaY;
        }

        double hostDeltaX = input.HostPoint2Feet.X - input.HostPoint1Feet.X;
        double hostDeltaY = input.HostPoint2Feet.Y - input.HostPoint1Feet.Y;
        double imageSpan = Math.Sqrt((imageDeltaX * imageDeltaX) + (imageDeltaY * imageDeltaY));
        double hostSpan = Math.Sqrt((hostDeltaX * hostDeltaX) + (hostDeltaY * hostDeltaY));
        if (imageSpan < MinimumImageSpanPixels)
        {
            throw new InvalidOperationException(
                "Контрольные точки изображения должны отличаться минимум на 1 пиксель.");
        }

        if (hostSpan < MinimumHostSpanFeet)
        {
            throw new InvalidOperationException(
                "Контрольные точки плиты расположены слишком близко друг к другу.");
        }

        double rotation = Math.Atan2(hostDeltaY, hostDeltaX)
            - Math.Atan2(imageDeltaY, imageDeltaX);
        return new IsoFieldPlanarTransform(
            input.ImagePoint1,
            input.HostPoint1Feet,
            hostSpan / imageSpan,
            NormalizeRadians(rotation),
            input.MirrorImageY);
    }

    private static void ValidateHostGeometry(IsoFieldHostGeometry hostGeometry)
    {
        if (hostGeometry is null)
        {
            throw new ArgumentNullException(nameof(hostGeometry));
        }

        if (hostGeometry.BoundaryLoopsFeet.Count == 0
            || hostGeometry.BoundaryLoopsFeet.Any(loop => loop.Count < 4))
        {
            throw new InvalidOperationException(
                "Контур верхней грани плиты не содержит замкнутых границ для привязки.");
        }
    }

    private static IEnumerable<IsoFieldPoint> BuildSamples(IReadOnlyList<IsoFieldPoint> points)
    {
        if (points.Count == 0)
        {
            yield break;
        }

        if (points.Count == 1)
        {
            yield return points[0];
            yield break;
        }

        for (int index = 0; index < points.Count - 1; index++)
        {
            IsoFieldPoint start = points[index];
            IsoFieldPoint end = points[index + 1];
            for (int sample = 0; sample < SegmentSampleCount; sample++)
            {
                double ratio = (double)sample / SegmentSampleCount;
                yield return new IsoFieldPoint(
                    start.X + ((end.X - start.X) * ratio),
                    start.Y + ((end.Y - start.Y) * ratio));
            }
        }

        yield return points[points.Count - 1];
    }

    private static bool IsInsideHost(
        IsoFieldPoint point,
        IReadOnlyList<IsoFieldPoint> outerBoundary,
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> holes)
    {
        return IsInsidePolygon(point, outerBoundary)
            && !holes.Any(hole => IsInsidePolygon(point, hole));
    }

    private static bool PolygonsOverlap(
        IReadOnlyList<IsoFieldPoint> first,
        IReadOnlyList<IsoFieldPoint> second)
    {
        if (first.Count < 3 || second.Count < 3)
        {
            return false;
        }

        return first.Any(point => IsInsidePolygon(point, second))
            || second.Any(point => IsInsidePolygon(point, first))
            || EnumerateSegments(first).Any(firstSegment =>
                EnumerateSegments(second).Any(secondSegment =>
                    SegmentsIntersect(
                        firstSegment.Start,
                        firstSegment.End,
                        secondSegment.Start,
                        secondSegment.End)));
    }

    private static IEnumerable<(IsoFieldPoint Start, IsoFieldPoint End)> EnumerateSegments(
        IReadOnlyList<IsoFieldPoint> polygon)
    {
        for (int index = 0; index < polygon.Count; index++)
        {
            IsoFieldPoint start = polygon[index];
            IsoFieldPoint end = polygon[(index + 1) % polygon.Count];
            if (start != end)
            {
                yield return (start, end);
            }
        }
    }

    private static bool SegmentsIntersect(
        IsoFieldPoint firstStart,
        IsoFieldPoint firstEnd,
        IsoFieldPoint secondStart,
        IsoFieldPoint secondEnd)
    {
        double firstSideStart = Cross(firstStart, firstEnd, secondStart);
        double firstSideEnd = Cross(firstStart, firstEnd, secondEnd);
        double secondSideStart = Cross(secondStart, secondEnd, firstStart);
        double secondSideEnd = Cross(secondStart, secondEnd, firstEnd);
        if (OppositeSides(firstSideStart, firstSideEnd)
            && OppositeSides(secondSideStart, secondSideEnd))
        {
            return true;
        }

        return Math.Abs(firstSideStart) <= BoundaryToleranceFeet
                && IsOnSegment(secondStart, firstStart, firstEnd)
            || Math.Abs(firstSideEnd) <= BoundaryToleranceFeet
                && IsOnSegment(secondEnd, firstStart, firstEnd)
            || Math.Abs(secondSideStart) <= BoundaryToleranceFeet
                && IsOnSegment(firstStart, secondStart, secondEnd)
            || Math.Abs(secondSideEnd) <= BoundaryToleranceFeet
                && IsOnSegment(firstEnd, secondStart, secondEnd);
    }

    private static double Cross(IsoFieldPoint start, IsoFieldPoint end, IsoFieldPoint point)
    {
        return ((end.X - start.X) * (point.Y - start.Y))
            - ((end.Y - start.Y) * (point.X - start.X));
    }

    private static bool OppositeSides(double first, double second)
    {
        return first > BoundaryToleranceFeet && second < -BoundaryToleranceFeet
            || first < -BoundaryToleranceFeet && second > BoundaryToleranceFeet;
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
            if (IsOnSegment(point, start, end))
            {
                return true;
            }

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

    private static bool IsOnSegment(
        IsoFieldPoint point,
        IsoFieldPoint start,
        IsoFieldPoint end)
    {
        double cross = ((point.Y - start.Y) * (end.X - start.X))
            - ((point.X - start.X) * (end.Y - start.Y));
        if (Math.Abs(cross) > BoundaryToleranceFeet)
        {
            return false;
        }

        double dot = ((point.X - start.X) * (end.X - start.X))
            + ((point.Y - start.Y) * (end.Y - start.Y));
        if (dot < -BoundaryToleranceFeet)
        {
            return false;
        }

        double squaredLength = ((end.X - start.X) * (end.X - start.X))
            + ((end.Y - start.Y) * (end.Y - start.Y));
        if (squaredLength <= BoundaryToleranceFeet * BoundaryToleranceFeet)
        {
            double deltaX = point.X - start.X;
            double deltaY = point.Y - start.Y;
            return (deltaX * deltaX) + (deltaY * deltaY)
                <= BoundaryToleranceFeet * BoundaryToleranceFeet;
        }

        return dot <= squaredLength + BoundaryToleranceFeet;
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

    private static List<string> BuildDiagnostics(
        IsoFieldPlanarTransform transform,
        int zoneCount,
        int outsideZoneCount,
        double insideRatio,
        bool controlPointsInside,
        int holeCount,
        bool canProceed)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("ru-RU");
        List<string> diagnostics =
        [
            $"Двухточечная привязка: {transform.MillimetersPerPixel.ToString("0.###", culture)} мм/пикс; "
                + $"поворот {transform.RotationDegrees.ToString("0.##", culture)}°; "
                + $"отражение Y: {(transform.MirrorImageY ? "да" : "нет")}.",
            $"Контур плиты: отверстий {holeCount}. Внутри допустимой области: {(insideRatio * 100).ToString("0.#", culture)}% проверочных точек."
        ];
        if (!controlPointsInside)
        {
            diagnostics.Add("Одна или обе контрольные точки находятся вне допустимого контура плиты.");
        }

        if (outsideZoneCount > 0)
        {
            diagnostics.Add($"За контур плиты или в отверстия попадает зон: {outsideZoneCount} из {zoneCount}.");
        }

        diagnostics.Add(canProceed
            ? "Привязка прошла read-only проверку. Можно переходить к расчёту правил."
            : "Привязка требует исправления; расчёт правил для плиты заблокирован.");
        return diagnostics;
    }

    private static void ValidatePoint(IsoFieldPoint point, string label)
    {
        if (point is null || !IsFinite(point.X) || !IsFinite(point.Y))
        {
            throw new InvalidOperationException($"{label} должна содержать конечные координаты.");
        }
    }

    private static double NormalizeRadians(double value)
    {
        while (value > Math.PI)
        {
            value -= 2 * Math.PI;
        }

        while (value <= -Math.PI)
        {
            value += 2 * Math.PI;
        }

        return value;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
