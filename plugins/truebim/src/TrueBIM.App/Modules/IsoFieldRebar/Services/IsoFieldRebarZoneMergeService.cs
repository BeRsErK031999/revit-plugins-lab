using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldRebarZoneMergeService
{
    private const int StableIdHashByteCount = 8;
    private readonly IsoFieldPolygonClipService polygonService = new();
    private readonly IsoFieldSlabRebarLayoutService layoutService = new();

    public IsoFieldRebarZoneMerge CreateMerge(
        RebarRulePreviewResult preview,
        IReadOnlyList<string> sourceZoneIds)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        string[] normalizedIds = NormalizeSourceZoneIds(sourceZoneIds);
        RebarRulePreviewItem[] members = ResolveMembers(preview, normalizedIds);
        _ = BuildMergedItem(members, normalizedIds);
        return new IsoFieldRebarZoneMerge(
            BuildMergedZoneId(members[0].Rule.LayerRole!.Value, normalizedIds),
            normalizedIds);
    }

    public RebarRulePreviewResult Apply(
        RebarRulePreviewResult preview,
        IReadOnlyList<IsoFieldRebarZoneMerge> merges)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (merges is null)
        {
            throw new ArgumentNullException(nameof(merges));
        }

        if (preview.EngineeringSettings is null)
        {
            throw new InvalidOperationException(
                "Объединение зон доступно только для инженерной раскладки host.");
        }

        if (merges.Count == 0)
        {
            return preview;
        }

        HashSet<string> occupiedSourceIds = new(StringComparer.Ordinal);
        Dictionary<string, RebarRulePreviewItem> replacementByFirstId = new(StringComparer.Ordinal);
        foreach (IsoFieldRebarZoneMerge merge in merges)
        {
            string[] normalizedIds = NormalizeSourceZoneIds(merge.SourceZoneIds);
            foreach (string sourceZoneId in normalizedIds)
            {
                if (!occupiedSourceIds.Add(sourceZoneId))
                {
                    throw new InvalidOperationException(
                        $"Зона '{sourceZoneId}' включена более чем в одно инженерное объединение.");
                }
            }

            RebarRulePreviewItem[] members = ResolveMembers(preview, normalizedIds);
            string expectedMergedZoneId = BuildMergedZoneId(
                members[0].Rule.LayerRole!.Value,
                normalizedIds);
            if (!string.Equals(merge.MergedZoneId, expectedMergedZoneId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Состав инженерного объединения изменился. Снимите его и создайте заново.");
            }

            RebarRulePreviewItem mergedItem = BuildMergedItem(members, normalizedIds);
            string firstSourceId = preview.Items
                .First(item => normalizedIds.Contains(item.ZoneId, StringComparer.Ordinal))
                .ZoneId;
            replacementByFirstId[firstSourceId] = mergedItem;
        }

        List<RebarRulePreviewItem> items = new(preview.Items.Count);
        foreach (RebarRulePreviewItem item in preview.Items)
        {
            if (replacementByFirstId.TryGetValue(item.ZoneId, out RebarRulePreviewItem? replacement))
            {
                items.Add(replacement);
                continue;
            }

            if (!occupiedSourceIds.Contains(item.ZoneId))
            {
                items.Add(item);
            }
        }

        List<string> diagnostics = preview.EffectiveBaseDiagnostics.ToList();
        IReadOnlyList<IsoFieldSlabRebarSegment> segments = Array.Empty<IsoFieldSlabRebarSegment>();
        if (items.Any(item => item.IsIncluded && item.HasValidRule))
        {
            try
            {
                segments = layoutService.BuildSegments(items, preview.EngineeringSettings);
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(exception.Message);
            }
        }

        Dictionary<string, int> countByZone = segments
            .GroupBy(segment => segment.ZoneId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        items = items
            .Select(item => item with
            {
                EstimatedBarCount = item.IsIncluded
                    && countByZone.TryGetValue(item.ZoneId, out int count)
                        ? count
                        : 0
            })
            .ToList();

        return new RebarRulePreviewResult(
            items,
            diagnostics.Distinct(StringComparer.Ordinal).ToArray(),
            preview.EngineeringSettings,
            segments.Count,
            preview.EffectiveBaseDiagnostics);
    }

    private RebarRulePreviewItem BuildMergedItem(
        IReadOnlyList<RebarRulePreviewItem> members,
        IReadOnlyList<string> normalizedIds)
    {
        ValidateMembers(members);
        IReadOnlyList<IsoFieldPolygonRegion> regions = polygonService.UnionRegions(
            members.SelectMany(member => member.EffectiveRegions).ToArray());
        if (regions.Count != 1)
        {
            throw new InvalidOperationException(
                "Выбранные зоны не образуют один непрерывный регион. Объединять можно только касающиеся или пересекающиеся зоны.");
        }

        RebarRulePreviewItem first = members[0];
        double? maximumRequiredArea = members
            .Select(member => member.Rule.RequiredAreaSquareCentimetersPerMeter)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();
        RebarRule rule = first.Rule with
        {
            Name = $"Объединённое правило {normalizedIds.Count} зон",
            Note = "Инженерные зоны объединены пользователем до сравнения с моделью.",
            RequiredAreaSquareCentimetersPerMeter = maximumRequiredArea
        };
        string mergedZoneId = BuildMergedZoneId(first.Rule.LayerRole!.Value, normalizedIds);
        return new RebarRulePreviewItem(
            mergedZoneId,
            BuildMergedZoneName(members),
            rule,
            Array.Empty<string>(),
            regions,
            BaseDiagnostics: members
                .SelectMany(member => member.EffectiveBaseDiagnostics)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            IsIncluded: true,
            IsManuallyOverridden: members.Any(member => member.IsManuallyOverridden),
            SourceZoneIds: normalizedIds);
    }

    private static void ValidateMembers(IReadOnlyList<RebarRulePreviewItem> members)
    {
        RebarRulePreviewItem first = members[0];
        if (members.Any(member => !member.IsIncluded))
        {
            throw new InvalidOperationException(
                "Исключённую зону нельзя объединить. Сначала включите все выбранные зоны.");
        }

        if (members.Any(member => member.IsMerged))
        {
            throw new InvalidOperationException(
                "Готовое объединение нельзя включить в другое. Сначала разъедините его.");
        }

        if (members.Any(member => !member.HasValidRule || !member.Rule.IsEngineeringRule))
        {
            throw new InvalidOperationException(
                "Объединять можно только зоны с валидными инженерными правилами.");
        }

        if (members.Any(member => !HasSamePlacementAndRule(first.Rule, member.Rule)))
        {
            throw new InvalidOperationException(
                "Для объединения нужны одинаковые слой, грань, направление и сочетание арматуры. При необходимости сначала настройте правила выбранных зон.");
        }

        if (members.Any(member => member.EffectiveRegions.Count == 0))
        {
            throw new InvalidOperationException(
                "У одной из выбранных зон нет допустимой геометрии после отсечения по host.");
        }
    }

    private static bool HasSamePlacementAndRule(RebarRule expected, RebarRule actual)
    {
        return string.Equals(expected.HostKind, actual.HostKind, StringComparison.Ordinal)
            && expected.LayerRole == actual.LayerRole
            && expected.Face == actual.Face
            && string.Equals(
                expected.PlacementDirection,
                actual.PlacementDirection,
                StringComparison.OrdinalIgnoreCase)
            && expected.ReinforcementMode == actual.ReinforcementMode
            && expected.EffectiveComponents.SequenceEqual(actual.EffectiveComponents);
    }

    private static RebarRulePreviewItem[] ResolveMembers(
        RebarRulePreviewResult preview,
        IReadOnlyList<string> normalizedIds)
    {
        Dictionary<string, RebarRulePreviewItem> sourceById = preview.Items
            .ToDictionary(item => item.ZoneId, StringComparer.Ordinal);
        List<RebarRulePreviewItem> members = new(normalizedIds.Count);
        foreach (string sourceZoneId in normalizedIds)
        {
            if (!sourceById.TryGetValue(sourceZoneId, out RebarRulePreviewItem? item))
            {
                throw new InvalidOperationException(
                    $"Зона '{sourceZoneId}' отсутствует в текущей рассчитанной раскладке.");
            }

            members.Add(item);
        }

        return members.ToArray();
    }

    private static string[] NormalizeSourceZoneIds(IReadOnlyList<string> sourceZoneIds)
    {
        if (sourceZoneIds is null)
        {
            throw new ArgumentNullException(nameof(sourceZoneIds));
        }

        string[] normalized = sourceZoneIds
            .Where(zoneId => !string.IsNullOrWhiteSpace(zoneId))
            .Select(zoneId => zoneId.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(zoneId => zoneId, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length < 2)
        {
            throw new InvalidOperationException(
                "Для инженерного объединения выберите минимум две зоны.");
        }

        return normalized;
    }

    private static string BuildMergedZoneId(
        IsoFieldLayerRole layerRole,
        IReadOnlyList<string> normalizedIds)
    {
        string source = string.Join("|", new[] { layerRole.ToString() }.Concat(normalizedIds));
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(source));
        return "zone-merge-" + string.Concat(hash
            .Take(StableIdHashByteCount)
            .Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string BuildMergedZoneName(IReadOnlyList<RebarRulePreviewItem> members)
    {
        string[] names = members
            .Select(member => member.ZoneName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(2)
            .ToArray();
        string suffix = members.Count > names.Length
            ? $"; ещё {members.Count - names.Length}"
            : string.Empty;
        return $"Объединение {members.Count} зон: {string.Join("; ", names)}{suffix}";
    }
}
