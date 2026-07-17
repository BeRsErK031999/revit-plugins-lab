using System.Globalization;
using System.Text.RegularExpressions;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldRebarReviewService
{
    private static readonly Regex StableIdPattern = new(
        "^(?<layer>[^:]+):(?<zone>.+):c[0-9]+:r[0-9]+:b[0-9]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<IsoFieldRebarReviewRow> BuildRows(
        RebarRulePreviewResult preview,
        IsoFieldRecognitionResult recognition,
        IsoFieldRebarChangePlan? changePlan = null)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (recognition is null)
        {
            throw new ArgumentNullException(nameof(recognition));
        }

        Dictionary<string, double?> confidenceByZone = recognition.Polylines
            .GroupBy(polyline => polyline.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(polyline => polyline.Confidence).FirstOrDefault(),
                StringComparer.Ordinal);
        List<IsoFieldRebarReviewRow> rows = new(preview.Items.Count);
        HashSet<string> matchedChangeIds = new(StringComparer.Ordinal);

        foreach (RebarRulePreviewItem item in preview.Items
            .OrderBy(item => item.Rule.LayerRole)
            .ThenBy(item => item.ZoneName, StringComparer.CurrentCultureIgnoreCase))
        {
            string stableIdPrefix = $"{item.Rule.LayerRole}:{item.ZoneId}:";
            IsoFieldRebarChange[] changes = changePlan?.Changes
                .Where(change => change.StableId.StartsWith(stableIdPrefix, StringComparison.Ordinal))
                .ToArray()
                ?? Array.Empty<IsoFieldRebarChange>();
            foreach (IsoFieldRebarChange change in changes)
            {
                matchedChangeIds.Add(change.StableId);
            }

            IsoFieldRebarComponent[] components = item.Rule.EffectiveComponents.ToArray();
            IReadOnlyList<string> diagnostics = item.Diagnostics
                .Concat(changePlan?.Diagnostics ?? Array.Empty<string>())
                .Distinct(StringComparer.CurrentCulture)
                .ToArray();
            rows.Add(new IsoFieldRebarReviewRow(
                item.ZoneId,
                item.ZoneName,
                item.Rule.LayerRole,
                ResolveStatus(item, changePlan, changes),
                $"{item.Rule.PlacementDirection} · {FormatFace(item.Rule.Face)}",
                FormatReinforcement(components, item.Rule.ReinforcementLabel),
                FormatArea(item.Rule),
                item.EstimatedBarCount,
                ResolveConfidence(item, confidenceByZone),
                components
                    .Select(component => component.DiameterMillimeters)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray(),
                components
                    .Select(component => component.SpacingMillimeters)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray(),
                changes.Count(change => change.Kind == IsoFieldRebarChangeKind.Add),
                changes.Count(change => change.Kind == IsoFieldRebarChangeKind.Update),
                changes
                    .Where(change => change.Kind == IsoFieldRebarChangeKind.Delete)
                    .Sum(change => change.ExistingElementIds.Count),
                changes.Count(change => change.Kind == IsoFieldRebarChangeKind.Unchanged),
                diagnostics,
                item.IsIncluded,
                item.IsManuallyOverridden,
                item.EffectiveSourceZoneIds));
        }

        if (changePlan is not null)
        {
            AppendObsoleteRows(rows, changePlan, matchedChangeIds);
        }

        return rows;
    }

    public bool MatchesFilter(
        IsoFieldRebarReviewRow row,
        IsoFieldRebarReviewFilter filter)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        if (filter is null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        string search = filter.SearchText.Trim();
        return (string.IsNullOrWhiteSpace(search)
                || Contains(row.ZoneId, search)
                || Contains(row.ZoneName, search)
                || row.EffectiveSourceZoneIds.Any(zoneId => Contains(zoneId, search)))
            && (!filter.LayerRole.HasValue || row.LayerRole == filter.LayerRole)
            && (!filter.Status.HasValue || row.Status == filter.Status)
            && (!filter.DiameterMillimeters.HasValue
                || row.DiametersMillimeters.Any(value => SameNumber(value, filter.DiameterMillimeters.Value)))
            && (!filter.SpacingMillimeters.HasValue
                || row.SpacingsMillimeters.Any(value => SameNumber(value, filter.SpacingMillimeters.Value)))
            && (!filter.MinimumConfidence.HasValue
                || row.Confidence.HasValue
                    && row.Confidence.Value >= filter.MinimumConfidence.Value);
    }

    private static IsoFieldRebarReviewStatus ResolveStatus(
        RebarRulePreviewItem item,
        IsoFieldRebarChangePlan? changePlan,
        IReadOnlyList<IsoFieldRebarChange> changes)
    {
        if (!item.IsIncluded && (changePlan is null || changes.Count == 0))
        {
            return IsoFieldRebarReviewStatus.Excluded;
        }

        if (!item.HasValidRule || changePlan?.CanApply == false)
        {
            return IsoFieldRebarReviewStatus.Invalid;
        }

        if (changePlan is null)
        {
            return IsoFieldRebarReviewStatus.NotCompared;
        }

        IsoFieldRebarChangeKind[] kinds = changes
            .Select(change => change.Kind)
            .Distinct()
            .ToArray();
        if (kinds.Length != 1)
        {
            return kinds.Length == 0
                ? IsoFieldRebarReviewStatus.NotCompared
                : IsoFieldRebarReviewStatus.Mixed;
        }

        return kinds[0] switch
        {
            IsoFieldRebarChangeKind.Add => IsoFieldRebarReviewStatus.Add,
            IsoFieldRebarChangeKind.Update => IsoFieldRebarReviewStatus.Update,
            IsoFieldRebarChangeKind.Delete => IsoFieldRebarReviewStatus.Delete,
            IsoFieldRebarChangeKind.Unchanged => IsoFieldRebarReviewStatus.Unchanged,
            _ => IsoFieldRebarReviewStatus.Mixed
        };
    }

    private static double? ResolveConfidence(
        RebarRulePreviewItem item,
        IReadOnlyDictionary<string, double?> confidenceByZone)
    {
        double[] values = item.EffectiveSourceZoneIds
            .Select(zoneId => confidenceByZone.TryGetValue(zoneId, out double? confidence)
                ? confidence
                : null)
            .Where(confidence => confidence.HasValue)
            .Select(confidence => confidence!.Value)
            .ToArray();
        return values.Length != item.EffectiveSourceZoneIds.Count ? null : values.Min();
    }

    private static void AppendObsoleteRows(
        List<IsoFieldRebarReviewRow> rows,
        IsoFieldRebarChangePlan changePlan,
        ISet<string> matchedChangeIds)
    {
        foreach (IGrouping<string, IsoFieldRebarChange> group in changePlan.Changes
            .Where(change => change.Kind == IsoFieldRebarChangeKind.Delete
                && !matchedChangeIds.Contains(change.StableId))
            .Select(change => new
            {
                Change = change,
                Metadata = ParseStableId(change.StableId)
            })
            .GroupBy(
                item => $"{item.Metadata.LayerRole}:{item.Metadata.ZoneId}",
                item => item.Change,
                StringComparer.Ordinal))
        {
            StableIdMetadata metadata = ParseStableId(group.First().StableId);
            rows.Add(new IsoFieldRebarReviewRow(
                metadata.ZoneId,
                "Ранее созданная зона",
                metadata.LayerRole,
                IsoFieldRebarReviewStatus.Delete,
                "—",
                "—",
                "—",
                0,
                null,
                Array.Empty<double>(),
                Array.Empty<double>(),
                0,
                0,
                group.Sum(change => change.ExistingElementIds.Count),
                0,
                Array.Empty<string>()));
        }
    }

    private static StableIdMetadata ParseStableId(string stableId)
    {
        Match match = StableIdPattern.Match(stableId);
        if (!match.Success)
        {
            return new StableIdMetadata(null, stableId);
        }

        IsoFieldLayerRole? layerRole = Enum.TryParse(
            match.Groups["layer"].Value,
            ignoreCase: false,
            out IsoFieldLayerRole parsedRole)
            ? parsedRole
            : null;
        return new StableIdMetadata(layerRole, match.Groups["zone"].Value);
    }

    private static string FormatReinforcement(
        IReadOnlyList<IsoFieldRebarComponent> components,
        string? fallback)
    {
        if (components.Count == 0)
        {
            return fallback ?? "—";
        }

        return string.Join(
            " + ",
            components.Select(component =>
                $"Ø{FormatNumber(component.DiameterMillimeters)}/{FormatNumber(component.SpacingMillimeters)}"));
    }

    private static string FormatArea(RebarRule rule)
    {
        if (!rule.RequiredAreaSquareCentimetersPerMeter.HasValue
            || !rule.ProvidedAreaSquareCentimetersPerMeter.HasValue)
        {
            return "—";
        }

        return $"{FormatNumber(rule.RequiredAreaSquareCentimetersPerMeter.Value)} → "
            + $"{FormatNumber(rule.ProvidedAreaSquareCentimetersPerMeter.Value)} см²/м";
    }

    private static string FormatFace(IsoFieldRebarFace? face)
    {
        return face switch
        {
            IsoFieldRebarFace.Bottom => "низ",
            IsoFieldRebarFace.Top => "верх",
            _ => "не задано"
        };
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static bool Contains(string value, string search)
    {
        return value.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static bool SameNumber(double first, double second)
    {
        return Math.Abs(first - second) <= 0.001;
    }

    private sealed record StableIdMetadata(
        IsoFieldLayerRole? LayerRole,
        string ZoneId);
}
