using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class SlabRebarPlacementService
{
    private const string SlabHostKind = "Slab";
    private const double MillimetersPerFoot = 304.8;
    private const double MinimumTestLengthFeet = 0.5;
    private const double MaximumTestLengthFeet = 4.0;
    private const double MinimumOffsetFeet = 0.25;

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
            throw new InvalidOperationException("Bounding box плиты слишком мал для тестовой арматуры.");
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
}
