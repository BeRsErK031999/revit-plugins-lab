using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class WallRebarPlacementService
{
    private const string WallHostKind = "Wall";
    private const double MillimetersPerFoot = 304.8;
    private const double MinimumTestLengthFeet = 0.5;
    private const double MaximumTestLengthFeet = 4.0;
    private const double MinimumOffsetFeet = 0.25;
    private const double DirectionLayerClearanceMillimeters = 5;
    private readonly IsoFieldSlabRebarLayoutService engineeringLayoutService = new();

    public IReadOnlyList<IsoFieldRebarPlacement> BuildEngineeringPlacements(
        IsoFieldHostGeometry hostGeometry,
        double wallThicknessFeet,
        RebarRulePreviewResult preview)
    {
        if (hostGeometry is null)
        {
            throw new ArgumentNullException(nameof(hostGeometry));
        }

        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (!preview.IsEngineeringPreview || preview.EngineeringSettings is null)
        {
            throw new InvalidOperationException("Для раскладки стены нужен валидный инженерный preview.");
        }

        if (preview.ActiveItems.Any(item =>
            !string.Equals(item.Rule.HostKind, WallHostKind, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Инженерная раскладка стены содержит правило другого типа host.");
        }

        if (!IsFinite(wallThicknessFeet) || wallThicknessFeet <= 0)
        {
            throw new InvalidOperationException("Не удалось определить положительную толщину стены.");
        }

        IReadOnlyList<IsoFieldSlabRebarSegment> segments = engineeringLayoutService.BuildSegments(
            preview.Items,
            preview.EngineeringSettings);
        Dictionary<IsoFieldRebarFace, double> maximumXDiameterByFace = segments
            .Where(segment => segment.Direction == IsoFieldRebarDirection.X)
            .GroupBy(segment => segment.Face)
            .ToDictionary(
                group => group.Key,
                group => group.Max(segment => segment.Component.DiameterMillimeters));
        Dictionary<IsoFieldSlabRebarSegment, double> centerFromFaceBySegment = segments
            .ToDictionary(
                segment => segment,
                segment => ResolveCenterFromFaceMillimeters(
                    segment,
                    preview.EngineeringSettings,
                    maximumXDiameterByFace));
        double requiredThicknessMillimeters = Enum
            .GetValues(typeof(IsoFieldRebarFace))
            .Cast<IsoFieldRebarFace>()
            .Where(face => face is IsoFieldRebarFace.Bottom or IsoFieldRebarFace.Top)
            .Sum(face => segments
                .Where(segment => segment.Face == face)
                .Select(segment => centerFromFaceBySegment[segment]
                    + (segment.Component.DiameterMillimeters / 2))
                .DefaultIfEmpty(0)
                .Max());
        if (segments.Select(segment => segment.Face).Distinct().Count() > 1)
        {
            requiredThicknessMillimeters += DirectionLayerClearanceMillimeters;
        }

        if ((wallThicknessFeet * MillimetersPerFoot) <= requiredThicknessMillimeters)
        {
            throw new InvalidOperationException(
                $"Толщины стены недостаточно для защитного слоя и разнесения четырёх слоёв. Нужно больше {requiredThicknessMillimeters:0.#} мм.");
        }

        Dictionary<string, RebarRulePreviewItem> itemsByZone = preview.Items
            .ToDictionary(item => item.ZoneId, StringComparer.Ordinal);
        List<IsoFieldRebarPlacement> placements = new(segments.Count);
        foreach (IsoFieldSlabRebarSegment segment in segments)
        {
            RebarRulePreviewItem item = itemsByZone[segment.ZoneId];
            double centerFromFaceFeet = centerFromFaceBySegment[segment] / MillimetersPerFoot;
            double planeOffsetFeet = segment.Face == IsoFieldRebarFace.Top
                ? -centerFromFaceFeet
                : -wallThicknessFeet + centerFromFaceFeet;
            placements.Add(new IsoFieldRebarPlacement(
                segment.ZoneId,
                item.ZoneName,
                item.Rule,
                ToWorldPoint(hostGeometry, segment.StartFeet, planeOffsetFeet),
                ToWorldPoint(hostGeometry, segment.EndFeet, planeOffsetFeet),
                hostGeometry.Normal,
                segment.Component,
                segment.StableId));
        }

        return placements;
    }

    public IReadOnlyList<IsoFieldRebarPlacement> BuildPlacements(
        IsoFieldWallPlacementFrame frame,
        IReadOnlyList<RebarRulePreviewItem> previewItems)
    {
        ValidateFrame(frame);
        if (previewItems is null)
        {
            throw new ArgumentNullException(nameof(previewItems));
        }

        RebarRulePreviewItem[] wallItems = previewItems
            .Where(item => item.IsIncluded
                && item.HasValidRule
                && string.Equals(item.Rule.HostKind, WallHostKind, StringComparison.Ordinal))
            .ToArray();
        if (wallItems.Length == 0)
        {
            throw new InvalidOperationException("Для стены нет валидных правил армирования.");
        }

        bool vertical = IsVertical(wallItems[0].Rule.PlacementDirection);
        IsoFieldRebarPoint3D axis = Normalize(frame.Axis);
        IsoFieldRebarPoint3D normal = Normalize(frame.Normal);
        IsoFieldRebarPoint3D lineDirection = vertical
            ? new IsoFieldRebarPoint3D(0, 0, 1)
            : axis;
        IsoFieldRebarPoint3D offsetDirection = vertical
            ? axis
            : new IsoFieldRebarPoint3D(0, 0, 1);
        double availableLength = vertical ? frame.HeightFeet : frame.LengthFeet;
        double crossSpan = vertical ? frame.LengthFeet : frame.HeightFeet;
        double halfLength = ResolveHalfLengthFeet(availableLength);
        double offsetStep = ResolveOffsetStepFeet(crossSpan, wallItems);
        List<IsoFieldRebarPlacement> placements = new();

        for (int index = 0; index < wallItems.Length; index++)
        {
            RebarRulePreviewItem item = wallItems[index];
            double offset = (index - ((wallItems.Length - 1) / 2.0)) * offsetStep;
            IsoFieldRebarPoint3D center = Add(frame.Center, Multiply(offsetDirection, offset));
            IsoFieldRebarPoint3D start = Add(center, Multiply(lineDirection, -halfLength));
            IsoFieldRebarPoint3D end = Add(center, Multiply(lineDirection, halfLength));

            placements.Add(new IsoFieldRebarPlacement(
                item.ZoneId,
                item.ZoneName,
                item.Rule,
                start,
                end,
                normal));
        }

        return placements;
    }

    private static void ValidateFrame(IsoFieldWallPlacementFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (frame.Center is null || frame.Axis is null || frame.Normal is null)
        {
            throw new InvalidOperationException("Геометрия стены должна содержать центр, ось и нормаль.");
        }

        double[] values =
        [
            frame.Center.XFeet,
            frame.Center.YFeet,
            frame.Center.ZFeet,
            frame.Axis.XFeet,
            frame.Axis.YFeet,
            frame.Axis.ZFeet,
            frame.Normal.XFeet,
            frame.Normal.YFeet,
            frame.Normal.ZFeet,
            frame.LengthFeet,
            frame.HeightFeet
        ];
        if (values.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
        {
            throw new InvalidOperationException("Геометрия стены должна содержать конечные числа.");
        }

        if (Length(frame.Axis) < 1e-9 || Length(frame.Normal) < 1e-9)
        {
            throw new InvalidOperationException("Ось и нормаль стены должны быть ненулевыми.");
        }

        if (frame.LengthFeet < MinimumTestLengthFeet || frame.HeightFeet < MinimumTestLengthFeet)
        {
            throw new InvalidOperationException("Геометрия стены слишком мала для пробного армирования.");
        }
    }

    private static bool IsVertical(string placementDirection)
    {
        return string.Equals(placementDirection, "Vertical", StringComparison.OrdinalIgnoreCase);
    }

    private static double ResolveHalfLengthFeet(double availableLengthFeet)
    {
        double targetLength = Math.Min(MaximumTestLengthFeet, availableLengthFeet * 0.8);
        targetLength = Math.Max(MinimumTestLengthFeet, targetLength);
        return targetLength / 2;
    }

    private static double ResolveOffsetStepFeet(
        double crossSpanFeet,
        IReadOnlyList<RebarRulePreviewItem> wallItems)
    {
        if (wallItems.Count <= 1)
        {
            return 0;
        }

        double requestedStep = wallItems
            .Select(item => item.Rule.SpacingMillimeters / MillimetersPerFoot)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0)
            .DefaultIfEmpty(MinimumOffsetFeet)
            .Min();
        requestedStep = Math.Max(MinimumOffsetFeet, requestedStep);

        double availableCrossSpan = crossSpanFeet * 0.8;
        double maximumStep = availableCrossSpan / (wallItems.Count - 1);
        return Math.Min(requestedStep, maximumStep);
    }

    private static double ResolveCenterFromFaceMillimeters(
        IsoFieldSlabRebarSegment segment,
        IsoFieldEngineeringSettings settings,
        IReadOnlyDictionary<IsoFieldRebarFace, double> maximumXDiameterByFace)
    {
        double center = settings.ConcreteCoverMillimeters
            + (segment.Component.DiameterMillimeters / 2);
        if (segment.Direction == IsoFieldRebarDirection.Y)
        {
            center += maximumXDiameterByFace.TryGetValue(segment.Face, out double xDiameter)
                ? xDiameter + DirectionLayerClearanceMillimeters
                : 0;
        }

        return center;
    }

    private static IsoFieldRebarPoint3D ToWorldPoint(
        IsoFieldHostGeometry geometry,
        IsoFieldPoint localPoint,
        double normalOffsetFeet)
    {
        return new IsoFieldRebarPoint3D(
            geometry.OriginFeet.XFeet
                + (geometry.AxisX.XFeet * localPoint.X)
                + (geometry.AxisY.XFeet * localPoint.Y)
                + (geometry.Normal.XFeet * normalOffsetFeet),
            geometry.OriginFeet.YFeet
                + (geometry.AxisX.YFeet * localPoint.X)
                + (geometry.AxisY.YFeet * localPoint.Y)
                + (geometry.Normal.YFeet * normalOffsetFeet),
            geometry.OriginFeet.ZFeet
                + (geometry.AxisX.ZFeet * localPoint.X)
                + (geometry.AxisY.ZFeet * localPoint.Y)
                + (geometry.Normal.ZFeet * normalOffsetFeet));
    }

    private static IsoFieldRebarPoint3D Add(
        IsoFieldRebarPoint3D first,
        IsoFieldRebarPoint3D second)
    {
        return new IsoFieldRebarPoint3D(
            first.XFeet + second.XFeet,
            first.YFeet + second.YFeet,
            first.ZFeet + second.ZFeet);
    }

    private static IsoFieldRebarPoint3D Multiply(
        IsoFieldRebarPoint3D point,
        double factor)
    {
        return new IsoFieldRebarPoint3D(
            point.XFeet * factor,
            point.YFeet * factor,
            point.ZFeet * factor);
    }

    private static IsoFieldRebarPoint3D Normalize(IsoFieldRebarPoint3D point)
    {
        double length = Length(point);
        return new IsoFieldRebarPoint3D(
            point.XFeet / length,
            point.YFeet / length,
            point.ZFeet / length);
    }

    private static double Length(IsoFieldRebarPoint3D point)
    {
        return Math.Sqrt(
            Math.Pow(point.XFeet, 2)
            + Math.Pow(point.YFeet, 2)
            + Math.Pow(point.ZFeet, 2));
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
