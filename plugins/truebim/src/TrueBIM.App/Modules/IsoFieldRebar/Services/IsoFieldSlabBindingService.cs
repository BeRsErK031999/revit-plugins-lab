using System.Globalization;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSlabBindingService
{
    private const double MinimumImageSpanPixels = 1;
    private const double MinimumHostSpanFeet = 0.01;
    private const double BoundaryToleranceFeet = 1e-6;
    private const double ThirdPointToleranceMillimeters = 50;
    private readonly IsoFieldPolygonClipService polygonClipService = new();

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
        ThirdPointCheck thirdPoint = ValidateThirdPoint(input, transform);
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
        IsoFieldClippedZone[] clippedZones = mappedZones
            .Select(zone => polygonClipService.Clip(zone, outerBoundary, holes))
            .ToArray();
        string[] clippedZoneIds = clippedZones
            .Where(zone => zone.WasClipped)
            .Select(zone => zone.SourceZoneId)
            .ToArray();
        string[] removedZoneIds = clippedZones
            .Where(zone => zone.IsEmpty)
            .Select(zone => zone.SourceZoneId)
            .ToArray();
        string[] outsideZoneIds = clippedZoneIds
            .Concat(removedZoneIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        IReadOnlyList<IsoFieldPoint> controlPoints =
        [
            input.HostPoint1Feet,
            input.HostPoint2Feet,
            thirdPoint.HostPointFeet
        ];
        bool controlPointsInside = controlPoints.All(point =>
            IsInsideHost(point, outerBoundary, holes));
        double totalOriginalArea = clippedZones.Sum(zone => zone.OriginalAreaSquareFeet);
        double totalClippedArea = clippedZones.Sum(zone => zone.ClippedAreaSquareFeet);
        double retainedAreaRatio = totalOriginalArea <= BoundaryToleranceFeet * BoundaryToleranceFeet
            ? 0
            : Math.Max(0, Math.Min(1, totalClippedArea / totalOriginalArea));
        bool canProceed = recognitionResult.Polylines.Count > 0
            && removedZoneIds.Length == 0
            && controlPointsInside
            && thirdPoint.IsValid;
        List<string> diagnostics = BuildDiagnostics(
            transform,
            recognitionResult.Polylines.Count,
            clippedZoneIds.Length,
            removedZoneIds.Length,
            retainedAreaRatio,
            controlPointsInside,
            holes.Count,
            thirdPoint,
            canProceed);

        return new IsoFieldSlabBindingAnalysis(
            transform,
            hostGeometry,
            mappedZones,
            clippedZones,
            outerBoundary,
            holes,
            controlPoints,
            clippedZoneIds,
            removedZoneIds,
            outsideZoneIds,
            outsideZoneIds.Length,
            retainedAreaRatio,
            thirdPoint.DeviationMillimeters,
            ThirdPointToleranceMillimeters,
            thirdPoint.IsValid,
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

    private static ThirdPointCheck ValidateThirdPoint(
        IsoFieldSlabBindingInput input,
        IsoFieldPlanarTransform transform)
    {
        if (input.ImagePoint3 is null || input.HostPoint3Feet is null)
        {
            throw new InvalidOperationException(
                "Для независимой проверки привязки укажите третью точку на карте и на плите.");
        }

        ValidatePoint(input.ImagePoint3, "Третья точка изображения");
        ValidatePoint(input.HostPoint3Feet, "Третья точка плиты");
        double imageOffset = PerpendicularDistance(
            input.ImagePoint3,
            input.ImagePoint1,
            input.ImagePoint2);
        if (imageOffset < MinimumImageSpanPixels)
        {
            throw new InvalidOperationException(
                "Третья точка изображения должна находиться в стороне от линии первых двух минимум на 1 пиксель.");
        }

        double hostOffset = PerpendicularDistance(
            input.HostPoint3Feet,
            input.HostPoint1Feet,
            input.HostPoint2Feet);
        if (hostOffset < MinimumHostSpanFeet)
        {
            throw new InvalidOperationException(
                "Третья точка плиты должна находиться в стороне от линии первых двух контрольных точек.");
        }

        IsoFieldPoint expectedHostPoint = transform.Map(input.ImagePoint3);
        double deviationFeet = Distance(expectedHostPoint, input.HostPoint3Feet);
        double deviationMillimeters = deviationFeet * 304.8;
        return new ThirdPointCheck(
            input.HostPoint3Feet,
            deviationMillimeters,
            deviationMillimeters <= ThirdPointToleranceMillimeters);
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

    private static bool IsInsideHost(
        IsoFieldPoint point,
        IReadOnlyList<IsoFieldPoint> outerBoundary,
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> holes)
    {
        return IsInsidePolygon(point, outerBoundary)
            && !holes.Any(hole => IsInsidePolygon(point, hole));
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
            return Distance(point, start) <= BoundaryToleranceFeet;
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
        int clippedZoneCount,
        int removedZoneCount,
        double retainedAreaRatio,
        bool controlPointsInside,
        int holeCount,
        ThirdPointCheck thirdPoint,
        bool canProceed)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("ru-RU");
        List<string> diagnostics =
        [
            $"Привязка: {transform.MillimetersPerPixel.ToString("0.###", culture)} мм/пикс; "
                + $"поворот {transform.RotationDegrees.ToString("0.##", culture)}°; "
                + $"отражение Y: {(transform.MirrorImageY ? "да" : "нет")}.",
            $"Третья точка: отклонение {thirdPoint.DeviationMillimeters.ToString("0.#", culture)} мм "
                + $"при допуске {ThirdPointToleranceMillimeters.ToString("0.#", culture)} мм.",
            $"Геометрия плиты: отверстий {holeCount}; сохранено {(retainedAreaRatio * 100).ToString("0.#", culture)}% площади зон."
        ];
        if (!controlPointsInside)
        {
            diagnostics.Add("Одна или несколько контрольных точек находятся вне допустимого контура плиты.");
        }

        if (!thirdPoint.IsValid)
        {
            diagnostics.Add("Третья точка не подтверждает масштаб, поворот или зеркальность первых двух точек.");
        }

        if (clippedZoneCount > 0)
        {
            diagnostics.Add($"По контуру плиты и отверстиям обрезано зон: {clippedZoneCount} из {zoneCount}.");
        }

        if (removedZoneCount > 0)
        {
            diagnostics.Add($"Полностью вне допустимой области осталось зон: {removedZoneCount}. Они блокируют расчёт правил.");
        }

        diagnostics.Add(canProceed
            ? "Привязка и отсечение зон проверены. Можно переходить к расчёту правил."
            : "Привязка требует исправления; расчёт правил для плиты заблокирован.");
        return diagnostics;
    }

    private static double PerpendicularDistance(
        IsoFieldPoint point,
        IsoFieldPoint lineStart,
        IsoFieldPoint lineEnd)
    {
        double deltaX = lineEnd.X - lineStart.X;
        double deltaY = lineEnd.Y - lineStart.Y;
        double length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= BoundaryToleranceFeet)
        {
            return 0;
        }

        double cross = Math.Abs(
            (deltaX * (lineStart.Y - point.Y))
            - ((lineStart.X - point.X) * deltaY));
        return cross / length;
    }

    private static double Distance(IsoFieldPoint first, IsoFieldPoint second)
    {
        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
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

    private sealed record ThirdPointCheck(
        IsoFieldPoint HostPointFeet,
        double DeviationMillimeters,
        bool IsValid);
}
