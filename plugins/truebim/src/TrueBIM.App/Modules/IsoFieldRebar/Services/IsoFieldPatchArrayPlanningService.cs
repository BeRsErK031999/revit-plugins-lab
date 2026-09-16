using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

/// <summary>Планирует прямые массивы по исходным пятнам дополнительного армирования.</summary>
public sealed class IsoFieldPatchArrayPlanningService
{
    private const double MillimetersPerFoot = 304.8;
    private const double GeometryToleranceFeet = 1e-7;
    private const double AreaToleranceSquareFeet = 1e-7;
    private readonly IsoFieldPolygonClipService polygonService = new();

    /// <summary>
    /// Принимает исходные цветовые регионы, до удлинения стержней. Размеры анкеровки
    /// передаются явно в мм по диаметру; таблицы нормативных значений здесь нет.
    /// При переданном нормализаторе фактическая длина семейства учитывается до проверки границ.
    /// Выходные прямоугольники уже включают анкеровку и не должны повторно обрабатываться этим методом.
    /// </summary>
    public RebarRulePreviewResult Build(
        RebarRulePreviewResult preview,
        IsoFieldHostGeometry hostGeometry,
        IReadOnlyDictionary<double, double> anchorageLengthsMillimeters,
        Func<double, double>? normalizeLengthMillimeters = null)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (hostGeometry is null)
        {
            throw new ArgumentNullException(nameof(hostGeometry));
        }

        if (anchorageLengthsMillimeters is null)
        {
            throw new ArgumentNullException(nameof(anchorageLengthsMillimeters));
        }

        IsoFieldEngineeringSettings settings = preview.EngineeringSettings
            ?? throw new ArgumentException("Для планирования массивов нужны инженерные параметры.", nameof(preview));
        IReadOnlyList<string> settingsDiagnostics = new IsoFieldSlabRebarLayoutService().ValidateSettings(settings);
        if (settingsDiagnostics.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", settingsDiagnostics), nameof(preview));
        }

        IsoFieldPolygonRegion host = CreateHostRegion(hostGeometry);
        List<RebarRulePreviewItem> items = new();
        List<PatchMember> candidates = new();
        foreach (RebarRulePreviewItem item in preview.Items)
        {
            if (!item.IsIncluded || !item.HasValidRule || !item.Rule.IsEngineeringRule
                || !string.Equals(item.Rule.HostKind, "Slab", StringComparison.Ordinal))
            {
                items.Add(item);
                continue;
            }

            if (settings.Mode != IsoFieldReinforcementMode.AdditionalOverBase)
            {
                items.Add(WithDiagnostic(item, "Планирование общих пятен требует режима дополнительного усиления поверх базовой сетки."));
                continue;
            }

            if (item.EffectiveRegions.Count == 0)
            {
                items.Add(WithDiagnostic(item, "У пятна нет геометрии для планирования массива."));
                continue;
            }

            foreach (IsoFieldPolygonRegion region in item.EffectiveRegions)
            {
                candidates.Add(new PatchMember(item, region));
            }
        }

        foreach (IGrouping<PlacementKey, PatchMember> group in candidates.GroupBy(member => Key(member.Source)))
        {
            foreach (IReadOnlyList<PatchMember> patch in BuildConnectedPatches(group.ToArray()))
            {
                items.Add(BuildPatch(patch, host, settings, anchorageLengthsMillimeters, normalizeLengthMillimeters));
            }
        }

        return preview with
        {
            Items = items.OrderBy(item => item.Rule.LayerRole).ThenBy(item => item.ZoneId, StringComparer.Ordinal).ToArray(),
            EstimatedBarCount = items.Where(item => item.IsIncluded && item.HasValidRule).Sum(item => item.EstimatedBarCount)
        };
    }

    private RebarRulePreviewItem BuildPatch(
        IReadOnlyList<PatchMember> members,
        IsoFieldPolygonRegion host,
        IsoFieldEngineeringSettings settings,
        IReadOnlyDictionary<double, double> anchorageLengthsMillimeters,
        Func<double, double>? normalizeLengthMillimeters)
    {
        RebarRulePreviewItem first = members[0].Source;
        string[] sourceIds = members.SelectMany(member => member.Source.EffectiveSourceZoneIds)
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] baseDiagnostics = members.SelectMany(member => member.Source.EffectiveBaseDiagnostics)
            .Distinct(StringComparer.Ordinal).ToArray();
        List<string> diagnostics = baseDiagnostics.ToList();
        IsoFieldRebarComponent[] components = members.SelectMany(member => member.Source.Rule.EffectiveComponents).ToArray();
        IsoFieldRebarComponent selected = new(
            components.Max(component => component.DiameterMillimeters),
            components.Min(component => component.SpacingMillimeters),
            1,
            2);
        double providedAdditionalArea = selected.AreaSquareCentimetersPerMeter;
        double[] baseAreas = members.Select(member => Math.Max(0,
            member.Source.Rule.ProvidedAreaSquareCentimetersPerMeter!.Value - AdditionalArea(member.Source))).ToArray();
        double providedArea = providedAdditionalArea + baseAreas.Min();
        double requiredArea = members.Max(member => member.Source.Rule.RequiredAreaSquareCentimetersPerMeter!.Value);
        // Числовая граница цветовой шкалы округлена; сохраняем тот же допуск,
        // по которому уже проверено исходное сочетание. Дополнительную арматуру не уменьшаем.
        if (providedAdditionalArea + 1e-8 < members.Max(member => AdditionalArea(member.Source))
            || providedArea + RebarRuleValidationService.AreaToleranceSquareCentimetersPerMeter
                + RebarRuleValidationService.NumericEpsilon < requiredArea)
        {
            diagnostics.Add("Один массив с выбранными максимальным диаметром и минимальным шагом не обеспечивает требуемую площадь. Необходимо отдельное сочетание армирования.");
        }

        bool hasAnchorage = anchorageLengthsMillimeters.TryGetValue(selected.DiameterMillimeters, out double anchorage)
            && IsFinite(anchorage) && anchorage > 0;
        if (!hasAnchorage)
        {
            diagnostics.Add($"Для Ø{selected.DiameterMillimeters:0.###} не задана подтверждённая длина анкеровки. Планирование этого пятна заблокировано.");
        }

        bool alongX = string.Equals(first.Rule.PlacementDirection, "X", StringComparison.OrdinalIgnoreCase);
        if (!alongX && !string.Equals(first.Rule.PlacementDirection, "Y", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add("Для прямого массива должно быть явно задано направление X или Y.");
        }

        double minimumAlong = double.PositiveInfinity;
        double maximumAlong = double.NegativeInfinity;
        double minimumCross = double.PositiveInfinity;
        double maximumCross = double.NegativeInfinity;
        foreach (PatchMember member in members)
        {
            double extension = hasAnchorage
                ? Math.Max(300, anchorage * AdditionalArea(member.Source) / providedAdditionalArea) / MillimetersPerFoot
                : 0;
            foreach (IsoFieldPoint point in member.Region.OuterBoundaryFeet)
            {
                double along = alongX ? point.X : point.Y;
                double cross = alongX ? point.Y : point.X;
                minimumAlong = Math.Min(minimumAlong, along - extension);
                maximumAlong = Math.Max(maximumAlong, along + extension);
                minimumCross = Math.Min(minimumCross, cross);
                maximumCross = Math.Max(maximumCross, cross);
            }
        }

        double minimumLength = settings.MinimumBarLengthMillimeters / MillimetersPerFoot;
        double lengthIncrease = Math.Max(0, minimumLength - (maximumAlong - minimumAlong));
        minimumAlong -= lengthIncrease / 2;
        maximumAlong += lengthIncrease / 2;
        if (normalizeLengthMillimeters is not null)
        {
            try
            {
                double requiredLengthMillimeters = (maximumAlong - minimumAlong) * MillimetersPerFoot;
                double fabricatedLengthMillimeters = normalizeLengthMillimeters(requiredLengthMillimeters);
                if (!IsFinite(fabricatedLengthMillimeters)
                    || fabricatedLengthMillimeters + (GeometryToleranceFeet * MillimetersPerFoot) < requiredLengthMillimeters)
                {
                    throw new InvalidOperationException("Нормализатор вернул недопустимую длину или уменьшил требуемую анкеровку.");
                }

                double fabricationIncrease = Math.Max(0,
                    (fabricatedLengthMillimeters - requiredLengthMillimeters) / MillimetersPerFoot);
                minimumAlong -= fabricationIncrease / 2;
                maximumAlong += fabricationIncrease / 2;
            }
            catch (Exception exception)
            {
                diagnostics.Add("Не удалось подобрать фактическую длину семейства: " + exception.Message);
            }
        }

        double spacing = selected.SpacingMillimeters / MillimetersPerFoot;
        int intervals = Math.Max(1, checked((int)Math.Ceiling((maximumCross - minimumCross - GeometryToleranceFeet) / spacing)));
        double widthIncrease = Math.Max(0, intervals * spacing - (maximumCross - minimumCross));
        minimumCross -= widthIncrease / 2;
        maximumCross += widthIncrease / 2;
        IsoFieldPolygonRegion rectangle = Rectangle(minimumAlong, maximumAlong, minimumCross, maximumCross, alongX);
        double insideArea = polygonService.IntersectRegions([rectangle], [host]).Sum(region => region.AreaSquareFeet);
        if (rectangle.AreaSquareFeet - insideArea > Math.Max(AreaToleranceSquareFeet, rectangle.AreaSquareFeet * 1e-6)
            || HasInsufficientBoundaryClearance(rectangle, host, settings.BoundaryOffsetMillimeters / MillimetersPerFoot))
        {
            diagnostics.Add("Прямой массив с требуемой анкеровкой выходит за допустимый контур плиты или пересекает отверстие. Требуется загиб у края либо разделение пятна; прямой массив не будет создан.");
        }

        if (intervals + 1 > settings.MaximumBarCount)
        {
            diagnostics.Add($"В пятне больше {settings.MaximumBarCount} стержней. Требуется разделение пятна.");
        }

        RebarRule rule = first.Rule with
        {
            Name = $"Правило общего пятна ({sourceIds.Length} исходных зон)",
            BarTypeName = selected.BarTypeName,
            SpacingMillimeters = selected.SpacingMillimeters,
            RequiredAreaSquareCentimetersPerMeter = requiredArea,
            ProvidedAreaSquareCentimetersPerMeter = providedArea,
            ReinforcementLabel = selected.DisplayName,
            Components = [selected],
            Note = "Общий прямой массив по пятну. Анкеровка от каждого уровня учитывает отношение площадей дополнительной арматуры; минимум 300 мм."
        };
        return new RebarRulePreviewItem(
            BuildPatchId(members),
            $"Пятно: {first.ZoneName}" + (sourceIds.Length > 1 ? $" (+{sourceIds.Length - 1})" : string.Empty),
            rule,
            diagnostics.Distinct(StringComparer.Ordinal).ToArray(),
            // Для заблокированного пятна сохраняем исходную геометрию, а не недопустимый массив.
            diagnostics.Count == 0 ? [rectangle] : members.Select(member => member.Region).ToArray(),
            diagnostics.Count == 0 ? intervals + 1 : 0,
            baseDiagnostics,
            IsIncluded: true,
            IsManuallyOverridden: members.Any(member => member.Source.IsManuallyOverridden),
            SourceZoneIds: sourceIds,
            IsArrayEnvelope: diagnostics.Count == 0);
    }

    private IReadOnlyList<IReadOnlyList<PatchMember>> BuildConnectedPatches(IReadOnlyList<PatchMember> members)
    {
        List<IReadOnlyList<PatchMember>> patches = new();
        HashSet<int> visited = new();
        for (int index = 0; index < members.Count; index++)
        {
            if (!visited.Add(index))
            {
                continue;
            }

            List<PatchMember> patch = new();
            Queue<int> pending = new();
            pending.Enqueue(index);
            while (pending.Count > 0)
            {
                PatchMember current = members[pending.Dequeue()];
                patch.Add(current);
                for (int other = 0; other < members.Count; other++)
                {
                    if (!visited.Contains(other) && AreConnected(current.Region, members[other].Region))
                    {
                        visited.Add(other);
                        pending.Enqueue(other);
                    }
                }
            }

            patches.Add(patch.OrderBy(member => member.Source.ZoneId, StringComparer.Ordinal).ToArray());
        }

        return patches;
    }

    private bool AreConnected(IsoFieldPolygonRegion first, IsoFieldPolygonRegion second)
    {
        IReadOnlyList<IsoFieldPoint> a = first.OuterBoundaryFeet;
        IReadOnlyList<IsoFieldPoint> b = second.OuterBoundaryFeet;
        if (a.Max(point => point.X) < b.Min(point => point.X) - GeometryToleranceFeet
            || b.Max(point => point.X) < a.Min(point => point.X) - GeometryToleranceFeet
            || a.Max(point => point.Y) < b.Min(point => point.Y) - GeometryToleranceFeet
            || b.Max(point => point.Y) < a.Min(point => point.Y) - GeometryToleranceFeet)
        {
            return false;
        }

        return polygonService.IntersectRegions([first], [second]).Sum(region => region.AreaSquareFeet) > AreaToleranceSquareFeet
            || MinimumBoundaryDistance(first, second) <= GeometryToleranceFeet;
    }

    private static bool HasInsufficientBoundaryClearance(
        IsoFieldPolygonRegion rectangle,
        IsoFieldPolygonRegion host,
        double clearance)
    {
        return clearance > GeometryToleranceFeet
            && MinimumBoundaryDistance(rectangle, host) + GeometryToleranceFeet < clearance;
    }

    private static double MinimumBoundaryDistance(IsoFieldPolygonRegion first, IsoFieldPolygonRegion second)
    {
        double minimum = double.PositiveInfinity;
        foreach (IReadOnlyList<IsoFieldPoint> a in Loops(first))
        {
            foreach (IReadOnlyList<IsoFieldPoint> b in Loops(second))
            {
                for (int i = 0; i < a.Count - 1; i++)
                {
                    for (int j = 0; j < b.Count - 1; j++)
                    {
                        minimum = Math.Min(minimum, SegmentDistance(a[i], a[i + 1], b[j], b[j + 1]));
                    }
                }
            }
        }

        return minimum;
    }

    private static double SegmentDistance(IsoFieldPoint a, IsoFieldPoint b, IsoFieldPoint c, IsoFieldPoint d)
    {
        double first = Cross(a, b, c);
        double second = Cross(a, b, d);
        double third = Cross(c, d, a);
        double fourth = Cross(c, d, b);
        if (((first > 0 && second < 0) || (first < 0 && second > 0))
            && ((third > 0 && fourth < 0) || (third < 0 && fourth > 0)))
        {
            return 0;
        }

        return Math.Min(Math.Min(PointSegmentDistance(a, c, d), PointSegmentDistance(b, c, d)),
            Math.Min(PointSegmentDistance(c, a, b), PointSegmentDistance(d, a, b)));
    }

    private static double PointSegmentDistance(IsoFieldPoint point, IsoFieldPoint start, IsoFieldPoint end)
    {
        double x = end.X - start.X;
        double y = end.Y - start.Y;
        double squared = (x * x) + (y * y);
        double t = squared < 1e-20 ? 0 : Math.Max(0, Math.Min(1, (((point.X - start.X) * x) + ((point.Y - start.Y) * y)) / squared));
        double dx = point.X - start.X - (t * x);
        double dy = point.Y - start.Y - (t * y);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double Cross(IsoFieldPoint a, IsoFieldPoint b, IsoFieldPoint c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static IEnumerable<IReadOnlyList<IsoFieldPoint>> Loops(IsoFieldPolygonRegion region) =>
        new[] { region.OuterBoundaryFeet }.Concat(region.HoleBoundariesFeet);

    private static double AdditionalArea(RebarRulePreviewItem item) =>
        item.Rule.EffectiveComponents.Sum(component => component.AreaSquareCentimetersPerMeter);

    private static IsoFieldPolygonRegion CreateHostRegion(IsoFieldHostGeometry geometry)
    {
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> loops = geometry.BoundaryLoopsFeet;
        if (loops.Count == 0 || loops.Any(loop => loop.Count < 4))
        {
            throw new ArgumentException("У плиты нет замкнутого внешнего контура.", nameof(geometry));
        }

        IReadOnlyList<IsoFieldPoint> outer = loops.OrderByDescending(loop => Math.Abs(SignedArea(loop))).First();
        IReadOnlyList<IsoFieldPoint>[] holes = loops.Where(loop => !ReferenceEquals(loop, outer)).ToArray();
        return new IsoFieldPolygonRegion(outer, holes, Math.Abs(SignedArea(outer)) - holes.Sum(hole => Math.Abs(SignedArea(hole))));
    }

    private static double SignedArea(IReadOnlyList<IsoFieldPoint> loop) =>
        Enumerable.Range(0, loop.Count - 1).Sum(index =>
            (loop[index].X * loop[index + 1].Y) - (loop[index + 1].X * loop[index].Y)) / 2;

    private static IsoFieldPolygonRegion Rectangle(double minAlong, double maxAlong, double minCross, double maxCross, bool alongX)
    {
        double minX = alongX ? minAlong : minCross;
        double maxX = alongX ? maxAlong : maxCross;
        double minY = alongX ? minCross : minAlong;
        double maxY = alongX ? maxCross : maxAlong;
        return new IsoFieldPolygonRegion(
            [new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY), new(minX, minY)],
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            (maxX - minX) * (maxY - minY));
    }

    private static RebarRulePreviewItem WithDiagnostic(RebarRulePreviewItem item, string diagnostic) =>
        item with { Diagnostics = [.. item.Diagnostics, diagnostic], EstimatedBarCount = 0 };

    private static string BuildPatchId(IReadOnlyList<PatchMember> members)
    {
        string source = string.Join("|", members.Select(member =>
            member.Source.ZoneId + ":" + string.Join(";", member.Region.OuterBoundaryFeet.Select(point =>
                point.X.ToString("R", CultureInfo.InvariantCulture) + "," + point.Y.ToString("R", CultureInfo.InvariantCulture))))
            .OrderBy(value => value, StringComparer.Ordinal));
        source = Key(members[0].Source) + "|" + source;
        using SHA256 sha256 = SHA256.Create();
        return "zone-patch-" + string.Concat(sha256.ComputeHash(Encoding.UTF8.GetBytes(source)).Take(8).Select(value => value.ToString("x2")));
    }

    private static PlacementKey Key(RebarRulePreviewItem item) =>
        new(item.Rule.LayerRole, item.Rule.Face, item.Rule.PlacementDirection.ToUpperInvariant(), item.Rule.ReinforcementMode);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private sealed record PatchMember(RebarRulePreviewItem Source, IsoFieldPolygonRegion Region);

    private sealed record PlacementKey(IsoFieldLayerRole? Layer, IsoFieldRebarFace? Face, string Direction, IsoFieldReinforcementMode? Mode);
}
