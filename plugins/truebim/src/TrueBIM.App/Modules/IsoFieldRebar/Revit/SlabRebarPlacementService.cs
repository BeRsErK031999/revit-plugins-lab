using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class SlabRebarPlacementService
{
    private const string SlabHostKind = "Slab";
    private const double MillimetersPerFoot = 304.8;
    private const double MinimumTestLengthFeet = 0.5;
    private const double MaximumTestLengthFeet = 4.0;
    private const double MinimumOffsetFeet = 0.25;
    private const double DirectionLayerClearanceMillimeters = 5;
    private readonly IsoFieldSlabRebarLayoutService engineeringLayoutService = new();

    public IReadOnlyList<IsoFieldRebarPlacement> BuildEngineeringPlacements(
        IsoFieldHostGeometry hostGeometry,
        double slabThicknessFeet,
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
            throw new InvalidOperationException("Для раскладки плиты нужен валидный инженерный preview.");
        }

        if (!IsFinite(slabThicknessFeet) || slabThicknessFeet <= 0)
        {
            throw new InvalidOperationException("Не удалось определить положительную толщину плиты.");
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

        if ((slabThicknessFeet * MillimetersPerFoot) <= requiredThicknessMillimeters)
        {
            throw new InvalidOperationException(
                $"Толщины плиты недостаточно для защитного слоя и разнесения четырёх слоёв. Нужно больше {requiredThicknessMillimeters:0.#} мм.");
        }

        Dictionary<string, RebarRulePreviewItem> itemsByZone = preview.Items
            .ToDictionary(item => item.ZoneId, StringComparer.Ordinal);
        List<IsoFieldRebarPlacement> placements = new(segments.Count);
        foreach (IsoFieldSlabRebarSegment segment in segments)
        {
            RebarRulePreviewItem item = itemsByZone[segment.ZoneId];
            double centerFromFaceMillimeters = centerFromFaceBySegment[segment];
            double centerFromFaceFeet = centerFromFaceMillimeters / MillimetersPerFoot;
            double planeOffsetFeet = segment.Face == IsoFieldRebarFace.Top
                ? -centerFromFaceFeet
                : -slabThicknessFeet + centerFromFaceFeet;
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

    public IReadOnlyList<IsoFieldRebarPlacement> BuildPlacements(
        IsoFieldRebarPlacementBounds bounds,
        IReadOnlyList<RebarRulePreviewItem> previewItems)
    {
        ValidateBounds(bounds);
        if (previewItems is null)
        {
            throw new ArgumentNullException(nameof(previewItems));
        }

        RebarRulePreviewItem[] slabItems = previewItems
            .Where(item => item.IsValid && string.Equals(item.Rule.HostKind, SlabHostKind, StringComparison.Ordinal))
            .ToArray();
        if (slabItems.Length == 0)
        {
            throw new InvalidOperationException("Для плиты нет валидных правил армирования.");
        }

        bool alongX = ResolveDirectionAlongX(bounds, slabItems[0].Rule.PlacementDirection);
        double availableLength = alongX ? bounds.WidthXFeet : bounds.WidthYFeet;
        double crossSpan = alongX ? bounds.WidthYFeet : bounds.WidthXFeet;
        double halfLength = ResolveHalfLengthFeet(availableLength);
        double offsetStep = ResolveOffsetStepFeet(crossSpan, slabItems);
        IsoFieldRebarPoint3D center = bounds.Center;
        List<IsoFieldRebarPlacement> placements = new();

        for (int index = 0; index < slabItems.Length; index++)
        {
            RebarRulePreviewItem item = slabItems[index];
            double offset = (index - ((slabItems.Length - 1) / 2.0)) * offsetStep;
            IsoFieldRebarPoint3D start = alongX
                ? new IsoFieldRebarPoint3D(center.XFeet - halfLength, center.YFeet + offset, center.ZFeet)
                : new IsoFieldRebarPoint3D(center.XFeet + offset, center.YFeet - halfLength, center.ZFeet);
            IsoFieldRebarPoint3D end = alongX
                ? new IsoFieldRebarPoint3D(center.XFeet + halfLength, center.YFeet + offset, center.ZFeet)
                : new IsoFieldRebarPoint3D(center.XFeet + offset, center.YFeet + halfLength, center.ZFeet);

            placements.Add(new IsoFieldRebarPlacement(
                item.ZoneId,
                item.ZoneName,
                item.Rule,
                start,
                end,
                new IsoFieldRebarPoint3D(0, 0, 1)));
        }

        return placements;
    }

    private static void ValidateBounds(IsoFieldRebarPlacementBounds bounds)
    {
        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }

        double[] values =
        [
            bounds.MinXFeet,
            bounds.MinYFeet,
            bounds.MinZFeet,
            bounds.MaxXFeet,
            bounds.MaxYFeet,
            bounds.MaxZFeet
        ];
        if (values.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
        {
            throw new InvalidOperationException("Границы плиты должны быть конечными числами.");
        }

        if (bounds.WidthXFeet < MinimumTestLengthFeet || bounds.WidthYFeet < MinimumTestLengthFeet)
        {
            throw new InvalidOperationException("Bounding box плиты слишком мал для пробного армирования.");
        }
    }

    private static bool ResolveDirectionAlongX(IsoFieldRebarPlacementBounds bounds, string placementDirection)
    {
        if (string.Equals(placementDirection, "X", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(placementDirection, "Y", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return bounds.WidthXFeet >= bounds.WidthYFeet;
    }

    private static double ResolveHalfLengthFeet(double availableLengthFeet)
    {
        double targetLength = Math.Min(MaximumTestLengthFeet, availableLengthFeet * 0.8);
        targetLength = Math.Max(MinimumTestLengthFeet, targetLength);
        return targetLength / 2;
    }

    private static double ResolveOffsetStepFeet(
        double crossSpanFeet,
        IReadOnlyList<RebarRulePreviewItem> slabItems)
    {
        if (slabItems.Count <= 1)
        {
            return 0;
        }

        double requestedStep = slabItems
            .Select(item => item.Rule.SpacingMillimeters / MillimetersPerFoot)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0)
            .DefaultIfEmpty(MinimumOffsetFeet)
            .Min();
        requestedStep = Math.Max(MinimumOffsetFeet, requestedStep);

        double availableCrossSpan = crossSpanFeet * 0.8;
        double maximumStep = availableCrossSpan / (slabItems.Count - 1);
        return Math.Min(requestedStep, maximumStep);
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

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
