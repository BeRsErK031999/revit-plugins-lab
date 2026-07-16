using System.Globalization;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed record IsoFieldZoneCorrection(
    string PolylineId,
    bool IsIncluded,
    int? LegendBandIndex);

public sealed record IsoFieldZoneMerge(IReadOnlyList<string> PolylineIds);

public sealed class IsoFieldZoneCorrectionService
{
    public IsoFieldRecognitionResult Apply(
        IsoFieldRecognitionResult source,
        IReadOnlyList<IsoFieldZoneCorrection> corrections,
        IReadOnlyList<IsoFieldZoneMerge> merges)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (corrections is null)
        {
            throw new ArgumentNullException(nameof(corrections));
        }

        if (merges is null)
        {
            throw new ArgumentNullException(nameof(merges));
        }

        Dictionary<string, IsoFieldPolyline> sourceById = BuildSourceIndex(source.Polylines);
        Dictionary<string, IsoFieldZoneCorrection> correctionById = BuildCorrectionIndex(
            corrections,
            sourceById);
        List<IsoFieldPolyline> corrected = new();
        int excludedCount = 0;
        int reclassifiedCount = 0;
        foreach (IsoFieldPolyline polyline in source.Polylines)
        {
            if (!correctionById.TryGetValue(polyline.Id, out IsoFieldZoneCorrection? correction))
            {
                corrected.Add(polyline);
                continue;
            }

            if (!correction.IsIncluded)
            {
                excludedCount++;
                continue;
            }

            IsoFieldPolyline updated = ApplyClass(source, polyline, correction.LegendBandIndex);
            if (updated.LegendBandIndex != polyline.LegendBandIndex)
            {
                reclassifiedCount++;
            }

            corrected.Add(updated);
        }

        IReadOnlyList<IsoFieldPolyline> merged = ApplyMerges(corrected, merges);
        int mergedSourceCount = merges.Sum(
            merge => merge?.PolylineIds?.Distinct(StringComparer.Ordinal).Count() ?? 0);
        List<string> diagnostics = source.Diagnostics.ToList();
        diagnostics.Add(
            $"Ручная коррекция зон: исключено {excludedCount}; изменён класс у {reclassifiedCount}; "
            + $"объединено групп {merges.Count} (исходных зон {mergedSourceCount}); итоговых зон {merged.Count}.");

        return new IsoFieldRecognitionResult(merged, diagnostics, source.EffectiveLegends);
    }

    public static string BuildZoneName(IsoFieldLegendBand band)
    {
        if (band is null)
        {
            throw new ArgumentNullException(nameof(band));
        }

        return band.MinimumValue.HasValue && band.MaximumValue.HasValue
            ? $"{FormatValue(band.MinimumValue.Value)}–{FormatValue(band.MaximumValue.Value)} см²/м · {band.HexColor}"
            : $"Макс. уровень {band.Index + 1} · {band.HexColor}";
    }

    private static Dictionary<string, IsoFieldPolyline> BuildSourceIndex(
        IReadOnlyList<IsoFieldPolyline> polylines)
    {
        Dictionary<string, IsoFieldPolyline> result = new(StringComparer.Ordinal);
        foreach (IsoFieldPolyline polyline in polylines)
        {
            if (result.ContainsKey(polyline.Id))
            {
                throw new InvalidOperationException(
                    $"Нельзя корректировать зоны: идентификатор '{polyline.Id}' встречается несколько раз.");
            }

            result.Add(polyline.Id, polyline);
        }

        return result;
    }

    private static Dictionary<string, IsoFieldZoneCorrection> BuildCorrectionIndex(
        IReadOnlyList<IsoFieldZoneCorrection> corrections,
        IReadOnlyDictionary<string, IsoFieldPolyline> sourceById)
    {
        Dictionary<string, IsoFieldZoneCorrection> result = new(StringComparer.Ordinal);
        foreach (IsoFieldZoneCorrection correction in corrections)
        {
            if (correction is null || string.IsNullOrWhiteSpace(correction.PolylineId))
            {
                throw new InvalidOperationException("Коррекция зоны должна содержать идентификатор.");
            }

            if (!sourceById.ContainsKey(correction.PolylineId))
            {
                throw new InvalidOperationException(
                    $"Зона '{correction.PolylineId}' отсутствует в исходном результате.");
            }

            if (result.ContainsKey(correction.PolylineId))
            {
                throw new InvalidOperationException(
                    $"Для зоны '{correction.PolylineId}' задано несколько коррекций.");
            }

            result.Add(correction.PolylineId, correction);
        }

        return result;
    }

    private static IsoFieldPolyline ApplyClass(
        IsoFieldRecognitionResult source,
        IsoFieldPolyline polyline,
        int? requestedBandIndex)
    {
        if (!requestedBandIndex.HasValue || requestedBandIndex == polyline.LegendBandIndex)
        {
            return polyline;
        }

        IsoFieldLegend? legend = FindLegend(source, polyline.LayerRole);
        IsoFieldLegendBand? band = legend?.Bands.FirstOrDefault(
            item => item.Index == requestedBandIndex.Value);
        if (band is null)
        {
            throw new InvalidOperationException(
                $"Для зоны '{polyline.Id}' не найден класс шкалы {requestedBandIndex.Value + 1}.");
        }

        return polyline with
        {
            LegendBandIndex = band.Index,
            ZoneName = BuildZoneName(band)
        };
    }

    private static IsoFieldLegend? FindLegend(
        IsoFieldRecognitionResult source,
        IsoFieldLayerRole? layerRole)
    {
        IsoFieldLegend? exact = source.EffectiveLegends.FirstOrDefault(
            legend => legend.LayerRole == layerRole);
        if (exact is not null)
        {
            return exact;
        }

        return source.EffectiveLegends.Count == 1
            ? source.EffectiveLegends[0]
            : null;
    }

    private static IReadOnlyList<IsoFieldPolyline> ApplyMerges(
        IReadOnlyList<IsoFieldPolyline> corrected,
        IReadOnlyList<IsoFieldZoneMerge> merges)
    {
        if (merges.Count == 0)
        {
            return corrected.ToArray();
        }

        Dictionary<string, IsoFieldPolyline> byId = BuildSourceIndex(corrected);
        HashSet<string> mergedIds = new(StringComparer.Ordinal);
        Dictionary<string, IsoFieldPolyline> replacementByFirstId = new(StringComparer.Ordinal);
        HashSet<string> occupiedIds = new(byId.Keys, StringComparer.Ordinal);
        int mergeSequence = 1;
        foreach (IsoFieldZoneMerge merge in merges)
        {
            string[] ids = merge?.PolylineIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            if (ids.Length < 2)
            {
                throw new InvalidOperationException(
                    "Для объединения выберите минимум две включённые зоны.");
            }

            List<IsoFieldPolyline> members = new();
            foreach (string id in ids)
            {
                if (!byId.TryGetValue(id, out IsoFieldPolyline? member))
                {
                    throw new InvalidOperationException(
                        $"Зона '{id}' исключена или отсутствует и не может быть объединена.");
                }

                if (!mergedIds.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Зона '{id}' включена более чем в одну группу объединения.");
                }

                members.Add(member);
            }

            ValidateMergeMembers(members);
            string firstId = corrected.First(polyline => ids.Contains(polyline.Id, StringComparer.Ordinal)).Id;
            string mergedId;
            do
            {
                mergedId = $"manual-merge-{mergeSequence:000}";
                mergeSequence++;
            }
            while (!occupiedIds.Add(mergedId));

            IReadOnlyList<IsoFieldPoint> hull = BuildConvexHull(
                members.SelectMany(member => member.Points).ToArray());
            double? confidence = members.All(member => member.Confidence.HasValue)
                ? members.Min(member => member.Confidence!.Value)
                : null;
            IsoFieldPolyline first = members[0];
            replacementByFirstId[firstId] = new IsoFieldPolyline(
                mergedId,
                hull,
                first.ZoneName,
                confidence,
                first.LayerRole,
                first.LegendBandIndex);
        }

        List<IsoFieldPolyline> result = new();
        foreach (IsoFieldPolyline polyline in corrected)
        {
            if (replacementByFirstId.TryGetValue(polyline.Id, out IsoFieldPolyline? replacement))
            {
                result.Add(replacement);
                continue;
            }

            if (!mergedIds.Contains(polyline.Id))
            {
                result.Add(polyline);
            }
        }

        return result;
    }

    private static void ValidateMergeMembers(IReadOnlyList<IsoFieldPolyline> members)
    {
        IsoFieldPolyline first = members[0];
        if (members.Any(member => member.LayerRole != first.LayerRole))
        {
            throw new InvalidOperationException(
                "Объединять можно только зоны одного расчётного слоя.");
        }

        bool sameClass = members.All(member => member.LegendBandIndex == first.LegendBandIndex)
            && (first.LegendBandIndex.HasValue
                || members.All(member => string.Equals(
                    member.ZoneName,
                    first.ZoneName,
                    StringComparison.Ordinal)));
        if (!sameClass)
        {
            throw new InvalidOperationException(
                "Перед объединением назначьте выбранным зонам одинаковый класс.");
        }
    }

    private static IReadOnlyList<IsoFieldPoint> BuildConvexHull(
        IReadOnlyList<IsoFieldPoint> sourcePoints)
    {
        IsoFieldPoint[] points = sourcePoints
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToArray();
        if (points.Length < 3)
        {
            throw new InvalidOperationException(
                "Выбранные зоны не образуют замкнутый контур для объединения.");
        }

        List<IsoFieldPoint> hull = new();
        foreach (IsoFieldPoint point in points)
        {
            while (hull.Count >= 2
                && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        int lowerCount = hull.Count;
        for (int index = points.Length - 2; index >= 0; index--)
        {
            IsoFieldPoint point = points[index];
            while (hull.Count > lowerCount
                && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        hull.RemoveAt(hull.Count - 1);
        if (hull.Count < 3)
        {
            throw new InvalidOperationException(
                "Выбранные зоны лежат на одной линии и не могут быть объединены.");
        }

        hull.Add(hull[0]);
        return hull;
    }

    private static double Cross(IsoFieldPoint origin, IsoFieldPoint a, IsoFieldPoint b)
    {
        return ((a.X - origin.X) * (b.Y - origin.Y))
            - ((a.Y - origin.Y) * (b.X - origin.X));
    }

    private static string FormatValue(double value)
    {
        return value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
    }
}
