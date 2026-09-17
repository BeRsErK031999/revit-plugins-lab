using System.Globalization;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSlabBindingService
{
    private const double MinimumImageSpanPixels = 1;
    private const double MinimumHostSpanFeet = 0.01;
    private const double BoundaryToleranceFeet = 1e-6;
    private const double ThirdPointToleranceMillimeters = 50;
    private const double MaximumAxisScaleDifferencePercent = 15;
    private const double MaximumAngleDifferenceDegrees = 5;
    private const double MinimumRetainedZoneAreaRatio = 0.95;
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
        IsoFieldPlanarTransform similarityTransform = BuildSimilarityTransform(input);
        ThirdPointCheck thirdPoint = ValidateThirdPoint(input, similarityTransform);
        IsoFieldPlanarTransform transform = thirdPoint.IsValid
            ? BuildThreePointTransform(input, similarityTransform)
            : similarityTransform;
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
        bool hasEffectiveZones = clippedZones.Any(zone => !zone.IsEmpty);
        bool removedZonesAreAcceptable = removedZoneIds.Length == 0
            || retainedAreaRatio + 1e-9 >= MinimumRetainedZoneAreaRatio;
        bool canProceed = recognitionResult.Polylines.Count > 0
            && hasEffectiveZones
            && removedZonesAreAcceptable
            && controlPointsInside
            && thirdPoint.IsValid;
        List<string> diagnostics = BuildDiagnostics(
            transform,
            recognitionResult.Polylines.Count,
            clippedZoneIds.Length,
            removedZoneIds.Length,
            retainedAreaRatio,
            removedZonesAreAcceptable,
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
            controlPointsInside,
            diagnostics,
            canProceed);
    }

    public IsoFieldPlanarTransform BuildTransform(IsoFieldSlabBindingInput input)
    {
        IsoFieldPlanarTransform similarityTransform = BuildSimilarityTransform(input);
        if (input.ImagePoint3 is null || input.HostPoint3Feet is null)
        {
            return similarityTransform;
        }

        ThirdPointCheck thirdPoint = ValidateThirdPoint(input, similarityTransform);
        return thirdPoint.IsValid
            ? BuildThreePointTransform(input, similarityTransform)
            : similarityTransform;
    }

    private static IsoFieldPlanarTransform BuildSimilarityTransform(IsoFieldSlabBindingInput input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        ValidatePoint(input.ImagePoint1, "Первая точка изображения");
        ValidatePoint(input.ImagePoint2, "Вторая точка изображения");
        ValidatePoint(input.HostPoint1Feet, "Первая точка на конструкции");
        ValidatePoint(input.HostPoint2Feet, "Вторая точка на конструкции");

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
                "Первые две точки на конструкции расположены слишком близко друг к другу.");
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

    private static IsoFieldPlanarTransform BuildThreePointTransform(
        IsoFieldSlabBindingInput input,
        IsoFieldPlanarTransform similarityTransform)
    {
        return similarityTransform with
        {
            ImageAxisPoint2 = input.ImagePoint2,
            ImageAxisPoint3 = input.ImagePoint3!,
            HostAxisPoint2Feet = input.HostPoint2Feet,
            HostAxisPoint3Feet = input.HostPoint3Feet!
        };
    }

    private static ThirdPointCheck ValidateThirdPoint(
        IsoFieldSlabBindingInput input,
        IsoFieldPlanarTransform transform)
    {
        if (input.ImagePoint3 is null || input.HostPoint3Feet is null)
        {
            throw new InvalidOperationException(
                "Для проверки привязки укажите третью точку на карте и на конструкции.");
        }

        ValidatePoint(input.ImagePoint3, "Третья точка изображения");
        ValidatePoint(input.HostPoint3Feet, "Третья точка на конструкции");
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
                "Третья точка на конструкции должна находиться в стороне от линии первых двух контрольных точек.");
        }

        IsoFieldPoint expectedHostPoint = transform.Map(input.ImagePoint3);
        double deviationFeet = Distance(expectedHostPoint, input.HostPoint3Feet);
        double deviationMillimeters = deviationFeet * 304.8;
        IsoFieldPoint imageAxis2 = CreateImageAxis(
            input.ImagePoint1,
            input.ImagePoint2,
            input.MirrorImageY);
        IsoFieldPoint imageAxis3 = CreateImageAxis(
            input.ImagePoint1,
            input.ImagePoint3,
            input.MirrorImageY);
        IsoFieldPoint hostAxis2 = Subtract(input.HostPoint2Feet, input.HostPoint1Feet);
        IsoFieldPoint hostAxis3 = Subtract(input.HostPoint3Feet, input.HostPoint1Feet);
        double imageOrientation = Cross(imageAxis2, imageAxis3);
        double hostOrientation = Cross(hostAxis2, hostAxis3);
        bool orientationMatches = Math.Sign(imageOrientation) == Math.Sign(hostOrientation);
        double primaryScale = Length(hostAxis2) / Length(imageAxis2);
        double secondaryScale = Length(hostAxis3) / Length(imageAxis3);
        double maximumScale = Math.Max(primaryScale, secondaryScale);
        double axisScaleDifferencePercent = maximumScale <= 0
            ? double.PositiveInfinity
            : Math.Abs(primaryScale - secondaryScale) / maximumScale * 100;
        double angleDifferenceDegrees = Math.Abs(
            AngleBetween(imageAxis2, imageAxis3)
            - AngleBetween(hostAxis2, hostAxis3))
            * 180 / Math.PI;
        bool isValid = orientationMatches
            && axisScaleDifferencePercent <= MaximumAxisScaleDifferencePercent
            && angleDifferenceDegrees <= MaximumAngleDifferenceDegrees;
        return new ThirdPointCheck(
            input.HostPoint3Feet,
            deviationMillimeters,
            axisScaleDifferencePercent,
            angleDifferenceDegrees,
            orientationMatches,
            isValid,
            isValid && deviationMillimeters > ThirdPointToleranceMillimeters);
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
                "На опорной поверхности конструкции не удалось найти замкнутую внешнюю границу.");
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
        bool removedZonesAreAcceptable,
        bool controlPointsInside,
        int holeCount,
        ThirdPointCheck thirdPoint,
        bool canProceed)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("ru-RU");
        List<string> diagnostics =
        [
            $"Привязка по трём точкам: масштаб X {transform.MillimetersPerPixel.ToString("0.###", culture)} мм/точку; "
                + $"масштаб Y {transform.SecondaryMillimetersPerPixel.ToString("0.###", culture)} мм/точку; "
                + $"поворот {transform.RotationDegrees.ToString("0.##", culture)}°; "
                + $"вертикаль перевёрнута: {(transform.MirrorImageY ? "да" : "нет")}.",
            thirdPoint.IsValid
                ? thirdPoint.UsesAxisScaleCompensation
                    ? $"Третья точка принята. Различие масштаба X/Y {thirdPoint.AxisScaleDifferencePercent.ToString("0.#", culture)}% автоматически компенсировано."
                    : "Три контрольные точки задают согласованное положение карты."
                : $"Третья точка не согласована с первыми двумя: исходное отклонение {thirdPoint.DeviationMillimeters.ToString("0.#", culture)} мм; "
                    + $"различие масштаба осей {thirdPoint.AxisScaleDifferencePercent.ToString("0.#", culture)}%; "
                    + $"различие углов {thirdPoint.AngleDifferenceDegrees.ToString("0.#", culture)}°.",
            $"Конструкция: отверстий {holeCount}; после обрезки сохранено {(retainedAreaRatio * 100).ToString("0.#", culture)}% площади зон."
        ];
        if (!controlPointsInside)
        {
            diagnostics.Add("Одна или несколько контрольных точек находятся за границами выбранной конструкции.");
        }

        if (!thirdPoint.IsValid)
        {
            diagnostics.Add(thirdPoint.OrientationMatches
                ? $"Три точки искажают геометрию сильнее допустимого: разница масштаба должна быть не более {MaximumAxisScaleDifferencePercent.ToString("0.#", culture)}%, а угла — не более {MaximumAngleDifferenceDegrees.ToString("0.#", culture)}°. Проверьте, что выбраны соответствующие углы."
                : "Третья точка находится с противоположной стороны. Проверьте порядок углов или переключатель «Перевернуть карту по вертикали».");
        }

        if (clippedZoneCount > 0)
        {
            diagnostics.Add($"По границам конструкции и отверстиям обрезано зон: {clippedZoneCount} из {zoneCount}.");
        }

        if (removedZoneCount > 0)
        {
            diagnostics.Add(canProceed
                ? $"Полностью вне допустимой области исключено зон: {removedZoneCount}. Перед применением подтвердите предупреждение проверки."
                : $"Полностью вне допустимой области осталось зон: {removedZoneCount}.");
        }

        if (!removedZonesAreAcceptable)
        {
            diagnostics.Add(
                $"После обрезки сохранено меньше {(MinimumRetainedZoneAreaRatio * 100).ToString("0.#", culture)}% площади зон. "
                + "Проверьте контрольные точки, границы конструкции и отверстия.");
        }

        diagnostics.Add(canProceed
            ? "Привязка и отсечение зон проверены. Можно переходить к расчёту правил."
            : "Привязка требует исправления; расчёт раскладки пока недоступен.");
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

    private static IsoFieldPoint CreateImageAxis(
        IsoFieldPoint anchor,
        IsoFieldPoint point,
        bool mirrorImageY)
    {
        return new IsoFieldPoint(
            point.X - anchor.X,
            mirrorImageY
                ? anchor.Y - point.Y
                : point.Y - anchor.Y);
    }

    private static IsoFieldPoint Subtract(IsoFieldPoint point, IsoFieldPoint anchor)
    {
        return new IsoFieldPoint(point.X - anchor.X, point.Y - anchor.Y);
    }

    private static double Length(IsoFieldPoint vector)
    {
        return Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
    }

    private static double Cross(IsoFieldPoint first, IsoFieldPoint second)
    {
        return (first.X * second.Y) - (first.Y * second.X);
    }

    private static double AngleBetween(IsoFieldPoint first, IsoFieldPoint second)
    {
        double denominator = Length(first) * Length(second);
        if (denominator <= BoundaryToleranceFeet)
        {
            return 0;
        }

        double cosine = ((first.X * second.X) + (first.Y * second.Y)) / denominator;
        return Math.Acos(Math.Max(-1, Math.Min(1, cosine)));
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
        double AxisScaleDifferencePercent,
        double AngleDifferenceDegrees,
        bool OrientationMatches,
        bool IsValid,
        bool UsesAxisScaleCompensation);
}
