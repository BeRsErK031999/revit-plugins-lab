using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSlabRebarLayoutService
{
    private const double MillimetersPerFoot = 304.8;
    private const double GeometryToleranceFeet = 1e-7;

    public IReadOnlyList<IsoFieldSlabRebarSegment> BuildSegments(
        IReadOnlyList<RebarRulePreviewItem> previewItems,
        IsoFieldEngineeringSettings settings)
    {
        if (previewItems is null)
        {
            throw new ArgumentNullException(nameof(previewItems));
        }

        IReadOnlyList<string> settingsDiagnostics = ValidateSettings(settings);
        if (settingsDiagnostics.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", settingsDiagnostics));
        }

        List<IsoFieldSlabRebarSegment> segments = new();
        foreach (RebarRulePreviewItem item in previewItems.Where(item => item.IsValid))
        {
            if (!item.Rule.IsEngineeringRule
                || item.Rule.LayerRole is null
                || item.Rule.Face is not (IsoFieldRebarFace.Bottom or IsoFieldRebarFace.Top))
            {
                continue;
            }

            IsoFieldRebarDirection direction = string.Equals(
                item.Rule.PlacementDirection,
                "Y",
                StringComparison.OrdinalIgnoreCase)
                ? IsoFieldRebarDirection.Y
                : IsoFieldRebarDirection.X;
            for (int regionIndex = 0; regionIndex < item.EffectiveRegions.Count; regionIndex++)
            {
                IsoFieldPolygonRegion region = item.EffectiveRegions[regionIndex];
                foreach (IsoFieldRebarComponent component in item.Rule.EffectiveComponents)
                {
                    AppendRegionSegments(
                        segments,
                        item.ZoneId,
                        item.Rule.LayerRole.Value,
                        item.Rule.Face.Value,
                        direction,
                        component,
                        region,
                        regionIndex,
                        settings);
                    if (segments.Count > settings.MaximumBarCount)
                    {
                        throw new InvalidOperationException(
                            $"Раскладка содержит больше {settings.MaximumBarCount} стержней. Укрупните зоны или увеличьте шаг до создания в Revit.");
                    }
                }
            }
        }

        IGrouping<string, IsoFieldSlabRebarSegment>? duplicateLine = segments
            .GroupBy(segment => string.Join(
                "|",
                segment.Face,
                segment.Direction,
                Math.Round(segment.StartFeet.X, 6),
                Math.Round(segment.StartFeet.Y, 6),
                Math.Round(segment.EndFeet.X, 6),
                Math.Round(segment.EndFeet.Y, 6)))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateLine is not null)
        {
            throw new InvalidOperationException(
                $"Зона {duplicateLine.First().ZoneId} слишком узкая либо перекрывается с соседней зоной: найдены совпадающие стержни на одной грани.");
        }

        return segments;
    }

    public IReadOnlyList<string> ValidateSettings(IsoFieldEngineeringSettings? settings)
    {
        if (settings is null)
        {
            return ["Инженерные параметры раскладки не заданы."];
        }

        List<string> diagnostics = new();
        if (!IsFinite(settings.ConcreteCoverMillimeters)
            || settings.ConcreteCoverMillimeters < 10
            || settings.ConcreteCoverMillimeters > 100)
        {
            diagnostics.Add("Защитный слой должен быть в диапазоне 10–100 мм.");
        }

        if (!IsFinite(settings.BoundaryOffsetMillimeters)
            || settings.BoundaryOffsetMillimeters < 0
            || settings.BoundaryOffsetMillimeters > 300)
        {
            diagnostics.Add("Отступ от границ и отверстий должен быть в диапазоне 0–300 мм.");
        }

        if (!IsFinite(settings.MinimumBarLengthMillimeters)
            || settings.MinimumBarLengthMillimeters < 100
            || settings.MinimumBarLengthMillimeters > 5000)
        {
            diagnostics.Add("Минимальная длина стержня должна быть в диапазоне 100–5000 мм.");
        }

        if (settings.MaximumBarCount < 1 || settings.MaximumBarCount > 100000)
        {
            diagnostics.Add("Лимит количества стержней должен быть в диапазоне 1–100000.");
        }

        return diagnostics;
    }

    private static void AppendRegionSegments(
        List<IsoFieldSlabRebarSegment> result,
        string zoneId,
        IsoFieldLayerRole layerRole,
        IsoFieldRebarFace face,
        IsoFieldRebarDirection direction,
        IsoFieldRebarComponent component,
        IsoFieldPolygonRegion region,
        int regionIndex,
        IsoFieldEngineeringSettings settings)
    {
        bool alongX = direction == IsoFieldRebarDirection.X;
        double boundaryOffsetFeet = settings.BoundaryOffsetMillimeters / MillimetersPerFoot;
        double minimumLengthFeet = settings.MinimumBarLengthMillimeters / MillimetersPerFoot;
        double spacingFeet = component.SpacingMillimeters / MillimetersPerFoot;
        IReadOnlyList<IsoFieldPoint> outer = region.OuterBoundaryFeet;
        double minimumCross = outer.Min(point => alongX ? point.Y : point.X);
        double maximumCross = outer.Max(point => alongX ? point.Y : point.X);
        double phase = spacingFeet * component.CombinationIndex / Math.Max(1, component.CombinationCount);
        IReadOnlyList<double> positions = BuildCrossPositions(
            minimumCross,
            maximumCross,
            boundaryOffsetFeet,
            spacingFeet,
            phase);
        int barIndex = 0;
        foreach (double crossPosition in positions)
        {
            IReadOnlyList<double> intersections = CollectIntersections(
                region,
                crossPosition,
                alongX);
            for (int index = 0; index + 1 < intersections.Count; index += 2)
            {
                double startCoordinate = intersections[index] + boundaryOffsetFeet;
                double endCoordinate = intersections[index + 1] - boundaryOffsetFeet;
                if (endCoordinate - startCoordinate < minimumLengthFeet - GeometryToleranceFeet)
                {
                    continue;
                }

                IsoFieldPoint start = alongX
                    ? new IsoFieldPoint(startCoordinate, crossPosition)
                    : new IsoFieldPoint(crossPosition, startCoordinate);
                IsoFieldPoint end = alongX
                    ? new IsoFieldPoint(endCoordinate, crossPosition)
                    : new IsoFieldPoint(crossPosition, endCoordinate);
                string stableId = $"{layerRole}:{zoneId}:c{component.CombinationIndex}:r{regionIndex}:b{barIndex}";
                result.Add(new IsoFieldSlabRebarSegment(
                    zoneId,
                    layerRole,
                    face,
                    direction,
                    component,
                    start,
                    end,
                    stableId));
                barIndex++;
            }
        }
    }

    private static IReadOnlyList<double> BuildCrossPositions(
        double minimum,
        double maximum,
        double boundaryOffset,
        double spacing,
        double phase)
    {
        double start = minimum + boundaryOffset + phase;
        double end = maximum - boundaryOffset;
        double minimumAllowed = minimum + boundaryOffset;
        List<double> positions = new();
        for (double value = start; value <= end + GeometryToleranceFeet; value += spacing)
        {
            positions.Add(value);
        }

        if (positions.Count == 0 && end >= minimumAllowed - GeometryToleranceFeet)
        {
            positions.Add(Math.Min(end, Math.Max(minimumAllowed, start)));
        }

        return positions;
    }

    private static IReadOnlyList<double> CollectIntersections(
        IsoFieldPolygonRegion region,
        double crossPosition,
        bool alongX)
    {
        List<double> intersections = new();
        AppendLoopIntersections(intersections, region.OuterBoundaryFeet, crossPosition, alongX);
        foreach (IReadOnlyList<IsoFieldPoint> hole in region.HoleBoundariesFeet)
        {
            AppendLoopIntersections(intersections, hole, crossPosition, alongX);
        }

        return intersections.OrderBy(value => value).ToArray();
    }

    private static void AppendLoopIntersections(
        List<double> intersections,
        IReadOnlyList<IsoFieldPoint> loop,
        double crossPosition,
        bool alongX)
    {
        for (int index = 0; index < loop.Count - 1; index++)
        {
            IsoFieldPoint start = loop[index];
            IsoFieldPoint end = loop[index + 1];
            double startCross = alongX ? start.Y : start.X;
            double endCross = alongX ? end.Y : end.X;
            if ((startCross > crossPosition) == (endCross > crossPosition))
            {
                continue;
            }

            double ratio = (crossPosition - startCross) / (endCross - startCross);
            double coordinate = alongX
                ? start.X + ((end.X - start.X) * ratio)
                : start.Y + ((end.Y - start.Y) * ratio);
            intersections.Add(coordinate);
        }
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
