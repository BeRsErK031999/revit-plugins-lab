using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldRebarQualityService
{
    private const double SquareFeetToSquareMeters = 0.09290304;
    private const double AreaToleranceSquareFeet = 1e-6;
    private const double RequiredAreaTolerance = 1e-6;
    private const double FullCoverageRatio = 0.995;
    private readonly IsoFieldPolygonClipService polygonService = new();

    public IsoFieldRebarQualityResult Analyze(
        RebarRulePreviewResult preview,
        IsoFieldSlabBindingAnalysis slabBinding)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (slabBinding is null)
        {
            throw new ArgumentNullException(nameof(slabBinding));
        }

        try
        {
            List<IsoFieldRebarQualityIssue> issues = new();
            RebarRulePreviewItem[] activeItems = preview.ActiveItems
                .Where(item => item.Rule.LayerRole.HasValue)
                .ToArray();
            IReadOnlyList<IsoFieldPolygonRegion> hostRegions = polygonService.UnionRegions(
            [
                new IsoFieldPolygonRegion(
                    slabBinding.OuterBoundaryFeet,
                    slabBinding.HoleBoundariesFeet,
                    0)
            ]);
            double hostAreaSquareFeet = hostRegions.Sum(region => region.AreaSquareFeet);

            AddRequiredAreaIssues(activeItems, issues);
            AddSameLayerOverlapIssues(activeItems, issues);
            AddFinalGeometryIssues(activeItems, hostRegions, issues);
            IReadOnlyList<IsoFieldRebarLayerCoverage> coverage = BuildCoverage(
                activeItems,
                hostRegions,
                hostAreaSquareFeet,
                issues);
            AddBindingWarnings(slabBinding, issues);

            IsoFieldRebarQualityIssue[] orderedIssues = OrderIssues(issues);
            return new IsoFieldRebarQualityResult(
                orderedIssues,
                coverage,
                BuildFingerprint(orderedIssues, coverage));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            IsoFieldRebarQualityIssue[] issues =
            [
                new IsoFieldRebarQualityIssue(
                    IsoFieldRebarQualityCode.GeometryAnalysisFailed,
                    IsoFieldRebarQualitySeverity.Blocking,
                    "Не удалось проверить геометрию раскладки: " + exception.Message)
            ];
            return new IsoFieldRebarQualityResult(
                issues,
                Array.Empty<IsoFieldRebarLayerCoverage>(),
                BuildFingerprint(issues, Array.Empty<IsoFieldRebarLayerCoverage>()));
        }
    }

    private static IsoFieldRebarQualityIssue[] OrderIssues(
        IEnumerable<IsoFieldRebarQualityIssue> issues)
    {
        return issues
            .OrderBy(issue => issue.Severity)
            .ThenBy(issue => issue.LayerRole)
            .ThenBy(issue => issue.Code)
            .ThenBy(issue => string.Join("|", issue.EffectiveZoneIds), StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddRequiredAreaIssues(
        IReadOnlyList<RebarRulePreviewItem> items,
        ICollection<IsoFieldRebarQualityIssue> issues)
    {
        foreach (RebarRulePreviewItem item in items)
        {
            double? required = item.Rule.RequiredAreaSquareCentimetersPerMeter;
            double? provided = item.Rule.ProvidedAreaSquareCentimetersPerMeter;
            if (!required.HasValue || !provided.HasValue
                || provided.Value + RequiredAreaTolerance >= required.Value)
            {
                continue;
            }

            issues.Add(new IsoFieldRebarQualityIssue(
                IsoFieldRebarQualityCode.RequiredAreaDeficit,
                IsoFieldRebarQualitySeverity.Blocking,
                $"Зона {item.ZoneId}: принято {provided.Value:0.###} см²/м при требуемых {required.Value:0.###} см²/м.",
                item.Rule.LayerRole,
                item.EffectiveSourceZoneIds,
                provided,
                required));
        }
    }

    private void AddSameLayerOverlapIssues(
        IReadOnlyList<RebarRulePreviewItem> items,
        ICollection<IsoFieldRebarQualityIssue> issues)
    {
        foreach (IGrouping<IsoFieldLayerRole, RebarRulePreviewItem> layer in items
            .GroupBy(item => item.Rule.LayerRole!.Value))
        {
            RebarRulePreviewItem[] layerItems = layer
                .OrderBy(item => item.ZoneId, StringComparer.Ordinal)
                .ToArray();
            for (int firstIndex = 0; firstIndex < layerItems.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < layerItems.Length; secondIndex++)
                {
                    RebarRulePreviewItem first = layerItems[firstIndex];
                    RebarRulePreviewItem second = layerItems[secondIndex];
                    double overlapSquareFeet = polygonService.IntersectRegions(
                            first.EffectiveRegions,
                            second.EffectiveRegions)
                        .Sum(region => region.AreaSquareFeet);
                    if (overlapSquareFeet <= AreaToleranceSquareFeet)
                    {
                        continue;
                    }

                    double overlapSquareMeters = overlapSquareFeet * SquareFeetToSquareMeters;
                    issues.Add(new IsoFieldRebarQualityIssue(
                        IsoFieldRebarQualityCode.SameLayerOverlap,
                        IsoFieldRebarQualitySeverity.Blocking,
                        $"Слой {layer.Key}: зоны {first.ZoneId} и {second.ZoneId} пересекаются на {overlapSquareMeters:0.###} м².",
                        layer.Key,
                        first.EffectiveSourceZoneIds
                            .Concat(second.EffectiveSourceZoneIds)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(id => id, StringComparer.Ordinal)
                            .ToArray(),
                        overlapSquareMeters,
                        0));
                }
            }
        }
    }

    private void AddFinalGeometryIssues(
        IReadOnlyList<RebarRulePreviewItem> items,
        IReadOnlyList<IsoFieldPolygonRegion> hostRegions,
        ICollection<IsoFieldRebarQualityIssue> issues)
    {
        foreach (RebarRulePreviewItem item in items)
        {
            double itemArea = polygonService.UnionRegions(item.EffectiveRegions)
                .Sum(region => region.AreaSquareFeet);
            double insideArea = polygonService.IntersectRegions(item.EffectiveRegions, hostRegions)
                .Sum(region => region.AreaSquareFeet);
            double outsideArea = Math.Max(0, itemArea - insideArea);
            if (outsideArea <= AreaToleranceSquareFeet)
            {
                continue;
            }

            double outsideSquareMeters = outsideArea * SquareFeetToSquareMeters;
            issues.Add(new IsoFieldRebarQualityIssue(
                IsoFieldRebarQualityCode.FinalGeometryOutsideHost,
                IsoFieldRebarQualitySeverity.Blocking,
                $"Зона {item.ZoneId}: итоговый контур выходит за host на {outsideSquareMeters:0.###} м².",
                item.Rule.LayerRole,
                item.EffectiveSourceZoneIds,
                outsideSquareMeters,
                0));
        }
    }

    private IReadOnlyList<IsoFieldRebarLayerCoverage> BuildCoverage(
        IReadOnlyList<RebarRulePreviewItem> items,
        IReadOnlyList<IsoFieldPolygonRegion> hostRegions,
        double hostAreaSquareFeet,
        ICollection<IsoFieldRebarQualityIssue> issues)
    {
        List<IsoFieldRebarLayerCoverage> coverage = new();
        foreach (IsoFieldLayerRole layerRole in Enum.GetValues(typeof(IsoFieldLayerRole)))
        {
            RebarRulePreviewItem[] layerItems = items
                .Where(item => item.Rule.LayerRole == layerRole)
                .ToArray();
            double coveredAreaSquareFeet = layerItems.Length == 0
                ? 0
                : polygonService.IntersectRegions(
                        polygonService.UnionRegions(layerItems.SelectMany(item => item.EffectiveRegions).ToArray()),
                        hostRegions)
                    .Sum(region => region.AreaSquareFeet);
            double ratio = hostAreaSquareFeet <= AreaToleranceSquareFeet
                ? 0
                : Math.Max(0, Math.Min(1, coveredAreaSquareFeet / hostAreaSquareFeet));
            coverage.Add(new IsoFieldRebarLayerCoverage(
                layerRole,
                layerItems.Length,
                coveredAreaSquareFeet * SquareFeetToSquareMeters,
                hostAreaSquareFeet * SquareFeetToSquareMeters,
                ratio));

            if (layerItems.Length == 0)
            {
                issues.Add(new IsoFieldRebarQualityIssue(
                    IsoFieldRebarQualityCode.MissingLayerCoverage,
                    IsoFieldRebarQualitySeverity.Warning,
                    $"Слой {layerRole}: нет включённых зон; покрытие host равно 0%.",
                    layerRole));
            }
            else if (ratio + 1e-9 < FullCoverageRatio)
            {
                issues.Add(new IsoFieldRebarQualityIssue(
                    IsoFieldRebarQualityCode.PartialLayerCoverage,
                    IsoFieldRebarQualitySeverity.Warning,
                    $"Слой {layerRole}: зоны покрывают {ratio:P1} площади host.",
                    layerRole,
                    layerItems.SelectMany(item => item.EffectiveSourceZoneIds)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToArray(),
                    ratio,
                    FullCoverageRatio));
            }
        }

        return coverage;
    }

    private static void AddBindingWarnings(
        IsoFieldSlabBindingAnalysis slabBinding,
        ICollection<IsoFieldRebarQualityIssue> issues)
    {
        string[] outsideZoneIds = slabBinding.OutsideZoneIds
            .Concat(slabBinding.RemovedZoneIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (outsideZoneIds.Length > 0)
        {
            issues.Add(new IsoFieldRebarQualityIssue(
                IsoFieldRebarQualityCode.SourceZoneOutsideHost,
                IsoFieldRebarQualitySeverity.Warning,
                $"Исходные зоны вне host: {outsideZoneIds.Length}. Проверьте привязку и назначение опорной плоскости.",
                ZoneIds: outsideZoneIds,
                MeasuredValue: outsideZoneIds.Length,
                LimitValue: 0));
        }

        string[] clippedZoneIds = slabBinding.ClippedZoneIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (clippedZoneIds.Length > 0)
        {
            issues.Add(new IsoFieldRebarQualityIssue(
                IsoFieldRebarQualityCode.ZoneClippedByHost,
                IsoFieldRebarQualitySeverity.Warning,
                $"По границе host отсечено зон: {clippedZoneIds.Length}. Проверьте overlay перед применением.",
                ZoneIds: clippedZoneIds,
                MeasuredValue: clippedZoneIds.Length,
                LimitValue: 0));
        }
    }

    private static string BuildFingerprint(
        IReadOnlyList<IsoFieldRebarQualityIssue> issues,
        IReadOnlyList<IsoFieldRebarLayerCoverage> coverage)
    {
        StringBuilder source = new();
        foreach (IsoFieldRebarQualityIssue issue in issues)
        {
            source.Append((int)issue.Severity).Append('|')
                .Append((int)issue.Code).Append('|')
                .Append(issue.LayerRole?.ToString()).Append('|')
                .Append(string.Join(",", issue.EffectiveZoneIds.OrderBy(id => id, StringComparer.Ordinal))).Append('|')
                .Append(issue.MeasuredValue?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(issue.LimitValue?.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        foreach (IsoFieldRebarLayerCoverage layer in coverage.OrderBy(item => item.LayerRole))
        {
            source.Append((int)layer.LayerRole).Append('|')
                .Append(layer.IncludedZoneCount).Append('|')
                .Append(layer.CoveredAreaSquareMeters.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(layer.HostAreaSquareMeters.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(layer.CoverageRatio.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        using SHA256 sha256 = SHA256.Create();
        return string.Concat(sha256.ComputeHash(Encoding.UTF8.GetBytes(source.ToString()))
            .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
