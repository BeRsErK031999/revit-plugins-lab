using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldArrayRebarGroupingService
{
    private const double MillimetersPerFoot = 304.8;
    private const double GeometryToleranceFeet = 1.0 / MillimetersPerFoot;
    private const double DirectionTolerance = 1e-6;

    public IReadOnlyList<IsoFieldArrayRebarPlacement> BuildArrays(
        IReadOnlyList<IsoFieldRebarPlacement> placements)
    {
        if (placements is null)
        {
            throw new ArgumentNullException(nameof(placements));
        }

        PreparedPlacement[] prepared = placements
            .Select(Prepare)
            .OrderBy(item => item.Source.ZoneId, StringComparer.Ordinal)
            .ThenBy(item => item.Source.StableId, StringComparer.Ordinal)
            .ToArray();
        if (prepared.Length == 0)
        {
            return Array.Empty<IsoFieldArrayRebarPlacement>();
        }

        List<IsoFieldArrayRebarPlacement> arrays = new();
        Dictionary<string, int> nextArrayIndexBySourcePrefix = new(StringComparer.Ordinal);
        foreach (IGrouping<GroupKey, PreparedPlacement> group in prepared.GroupBy(BuildGroupKey))
        {
            PreparedPlacement[] ordered = group
                .OrderBy(item => Dot(item.Midpoint, item.CrossDirection))
                .ThenBy(item => item.Source.StableId, StringComparer.Ordinal)
                .ToArray();
            int startIndex = 0;
            for (int index = 1; index <= ordered.Length; index++)
            {
                bool continuesRun = index < ordered.Length
                    && AreConsecutive(ordered[index - 1], ordered[index]);
                if (continuesRun)
                {
                    continue;
                }

                PreparedPlacement[] run = ordered
                    .Skip(startIndex)
                    .Take(index - startIndex)
                    .ToArray();
                string sourcePrefix = RemoveBarIndex(run[0].Source.StableId!);
                int arrayIndex = nextArrayIndexBySourcePrefix.TryGetValue(sourcePrefix, out int nextIndex)
                    ? nextIndex
                    : 0;
                nextArrayIndexBySourcePrefix[sourcePrefix] = arrayIndex + 1;
                arrays.Add(CreateArray(run, arrayIndex));
                startIndex = index;
            }
        }

        return RepairSingletonArrays(arrays)
            .GroupBy(BuildFamilyInstanceKey)
            .Select(MergeCoincidentArrays)
            .OrderBy(item => item.StableId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<IsoFieldArrayRebarPlacement> RepairSingletonArrays(
        IReadOnlyList<IsoFieldArrayRebarPlacement> arrays)
    {
        List<IsoFieldArrayRebarPlacement> repaired = new(arrays.Count);
        foreach (IGrouping<SingletonRepairKey, IsoFieldArrayRebarPlacement> group in arrays
            .GroupBy(BuildSingletonRepairKey))
        {
            Vector direction = DirectionOf(group.First());
            Vector normal = ToVector(group.First().Normal).Normalize();
            Vector crossDirection = direction.Cross(normal).Normalize();
            IsoFieldArrayRebarPlacement[] ordered = group
                .OrderBy(item => Dot(item.FirstBarStart, crossDirection))
                .ThenBy(item => item.StableId, StringComparer.Ordinal)
                .ToArray();

            int sequenceStart = 0;
            for (int index = 1; index <= ordered.Length; index++)
            {
                bool continuesSequence = index < ordered.Length
                    && AreArraysConsecutive(ordered[index - 1], ordered[index], crossDirection);
                if (continuesSequence)
                {
                    continue;
                }

                RepairSingletonSequence(
                    ordered.Skip(sequenceStart).Take(index - sequenceStart).ToArray(),
                    repaired);
                sequenceStart = index;
            }
        }

        return repaired;
    }

    private static void RepairSingletonSequence(
        IReadOnlyList<IsoFieldArrayRebarPlacement> sequence,
        ICollection<IsoFieldArrayRebarPlacement> target)
    {
        List<IsoFieldArrayRebarPlacement> repaired = new(sequence.Count);
        for (int index = 0; index < sequence.Count; index++)
        {
            IsoFieldArrayRebarPlacement current = sequence[index];
            if (current.BarCount > 1)
            {
                repaired.Add(current);
                continue;
            }

            bool nextIsSingleton = index + 1 < sequence.Count
                && sequence[index + 1].BarCount == 1;
            if (nextIsSingleton)
            {
                repaired.Add(MergeSequentialArrays(current, sequence[index + 1]));
                index++;
                continue;
            }

            if (repaired.Count > 0)
            {
                repaired[repaired.Count - 1] = MergeSequentialArrays(
                    repaired[repaired.Count - 1],
                    current);
                continue;
            }

            if (index + 1 < sequence.Count)
            {
                repaired.Add(MergeSequentialArrays(current, sequence[index + 1]));
                index++;
                continue;
            }

            repaired.Add(current);
        }

        foreach (IsoFieldArrayRebarPlacement array in repaired)
        {
            target.Add(array);
        }
    }

    private static bool AreArraysConsecutive(
        IsoFieldArrayRebarPlacement previous,
        IsoFieldArrayRebarPlacement current,
        Vector crossDirection)
    {
        double actual = Dot(current.FirstBarStart, crossDirection)
            - Dot(previous.LastBarStart, crossDirection);
        double expected = previous.Component.SpacingMillimeters / MillimetersPerFoot;
        return actual > 0
            && Math.Abs(actual - expected) <= GeometryToleranceFeet;
    }

    private static IsoFieldArrayRebarPlacement MergeSequentialArrays(
        IsoFieldArrayRebarPlacement first,
        IsoFieldArrayRebarPlacement second)
    {
        Vector direction = DirectionOf(first);
        double startAlong = Math.Min(
            Dot(first.FirstBarStart, direction),
            Dot(second.FirstBarStart, direction));
        double endAlong = Math.Max(
            Dot(first.FirstBarEnd, direction),
            Dot(second.FirstBarEnd, direction));
        IsoFieldRebarPoint3D firstStart = ShiftAlong(
            first.FirstBarStart,
            direction,
            startAlong - Dot(first.FirstBarStart, direction));
        IsoFieldRebarPoint3D firstEnd = ShiftAlong(
            firstStart,
            direction,
            endAlong - startAlong);
        IsoFieldRebarPoint3D lastStart = ShiftAlong(
            second.LastBarStart,
            direction,
            startAlong - Dot(second.LastBarStart, direction));
        string[] sourceStableIds = first.SourceStableIds
            .Concat(second.SourceStableIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return first with
        {
            FirstBarStart = firstStart,
            FirstBarEnd = firstEnd,
            LastBarStart = lastStart,
            BarCount = first.BarCount + second.BarCount,
            SourceStableIds = sourceStableIds
        };
    }

    private static PreparedPlacement Prepare(IsoFieldRebarPlacement placement)
    {
        if (placement.Component is null || string.IsNullOrWhiteSpace(placement.StableId))
        {
            throw new InvalidOperationException(
                $"У расчётной зоны {placement.ZoneName} нет данных для создания семейства дополнительного армирования.");
        }

        Vector direction = Subtract(placement.End, placement.Start);
        double length = direction.Length;
        if (length <= GeometryToleranceFeet)
        {
            throw new InvalidOperationException(
                $"В зоне {placement.ZoneName} найден стержень нулевой длины.");
        }

        direction = direction.Scale(1 / length);
        IsoFieldRebarPoint3D start = placement.Start;
        IsoFieldRebarPoint3D end = placement.End;
        if (MustReverse(direction))
        {
            direction = direction.Scale(-1);
            start = placement.End;
            end = placement.Start;
        }

        Vector normal = ToVector(placement.Normal).Normalize();
        Vector crossDirection = direction.Cross(normal).Normalize();
        IsoFieldRebarPoint3D midpoint = Midpoint(start, end);
        return new PreparedPlacement(
            placement,
            start,
            end,
            midpoint,
            direction,
            normal,
            crossDirection,
            length);
    }

    private static GroupKey BuildGroupKey(PreparedPlacement item)
    {
        IsoFieldRebarComponent component = item.Source.Component!;
        return new GroupKey(
            item.Source.ZoneId,
            item.Source.Rule.LayerRole,
            item.Source.Rule.Face,
            item.Source.Rule.PlacementDirection.ToUpperInvariant(),
            component.DiameterMillimeters,
            component.SpacingMillimeters,
            component.CombinationIndex,
            component.CombinationCount,
            Quantize(item.Direction.X, DirectionTolerance),
            Quantize(item.Direction.Y, DirectionTolerance),
            Quantize(item.Direction.Z, DirectionTolerance),
            Quantize(item.Length, GeometryToleranceFeet),
            Quantize(Dot(item.Midpoint, item.Direction), GeometryToleranceFeet),
            Quantize(Dot(item.Midpoint, item.Normal), GeometryToleranceFeet));
    }

    private static bool AreConsecutive(PreparedPlacement previous, PreparedPlacement current)
    {
        double actual = Dot(current.Midpoint, previous.CrossDirection)
            - Dot(previous.Midpoint, previous.CrossDirection);
        double expected = previous.Source.Component!.SpacingMillimeters / MillimetersPerFoot;
        return actual > 0
            && Math.Abs(actual - expected) <= GeometryToleranceFeet;
    }

    private static IsoFieldArrayRebarPlacement CreateArray(
        IReadOnlyList<PreparedPlacement> run,
        int runIndex)
    {
        PreparedPlacement first = run[0];
        PreparedPlacement last = run[run.Count - 1];
        string sourcePrefix = RemoveBarIndex(first.Source.StableId!);
        return new IsoFieldArrayRebarPlacement(
            first.Source.ZoneId,
            first.Source.ZoneName,
            first.Source.Rule,
            first.Source.Component!,
            first.Start,
            first.End,
            last.Start,
            first.Source.Normal,
            run.Count,
            $"{sourcePrefix}:a{runIndex}",
            run.Select(item => item.Source.StableId!).ToArray());
    }

    private static SingletonRepairKey BuildSingletonRepairKey(
        IsoFieldArrayRebarPlacement placement)
    {
        Vector direction = DirectionOf(placement);
        Vector normal = ToVector(placement.Normal).Normalize();
        return new SingletonRepairKey(
            placement.ZoneId,
            placement.Rule.LayerRole,
            placement.Rule.Face,
            placement.Rule.PlacementDirection.ToUpperInvariant(),
            placement.Component.DiameterMillimeters,
            placement.Component.SpacingMillimeters,
            placement.Component.CombinationIndex,
            placement.Component.CombinationCount,
            Quantize(direction.X, DirectionTolerance),
            Quantize(direction.Y, DirectionTolerance),
            Quantize(direction.Z, DirectionTolerance),
            Quantize(Dot(placement.FirstBarStart, normal), GeometryToleranceFeet));
    }

    private static FamilyInstanceKey BuildFamilyInstanceKey(
        IsoFieldArrayRebarPlacement placement)
    {
        Vector direction = DirectionOf(placement);
        return new FamilyInstanceKey(
            placement.Rule.Face,
            placement.Rule.PlacementDirection.ToUpperInvariant(),
            placement.Component.DiameterMillimeters,
            placement.Component.SpacingMillimeters,
            Quantize(placement.FirstBarStart.XFeet, GeometryToleranceFeet),
            Quantize(placement.FirstBarStart.YFeet, GeometryToleranceFeet),
            Quantize(placement.FirstBarStart.ZFeet, GeometryToleranceFeet),
            Quantize(direction.X, DirectionTolerance),
            Quantize(direction.Y, DirectionTolerance),
            Quantize(direction.Z, DirectionTolerance));
    }

    private static IsoFieldArrayRebarPlacement MergeCoincidentArrays(
        IGrouping<FamilyInstanceKey, IsoFieldArrayRebarPlacement> group)
    {
        IsoFieldArrayRebarPlacement first = group
            .OrderBy(item => item.StableId, StringComparer.Ordinal)
            .First();
        Vector direction = DirectionOf(first);
        Vector normal = ToVector(first.Normal).Normalize();
        Vector crossDirection = direction.Cross(normal).Normalize();
        double maximumLength = group.Max(item => item.BarLengthFeet);
        double maximumWidth = group.Max(item => item.ArrayWidthFeet);
        string[] sourceStableIds = group
            .SelectMany(item => item.SourceStableIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return first with
        {
            FirstBarEnd = ShiftAlong(first.FirstBarStart, direction, maximumLength),
            LastBarStart = ShiftAlong(first.FirstBarStart, crossDirection, maximumWidth),
            BarCount = group.Max(item => item.BarCount),
            SourceStableIds = sourceStableIds
        };
    }

    private static Vector DirectionOf(IsoFieldArrayRebarPlacement placement)
    {
        return new Vector(
            placement.FirstBarEnd.XFeet - placement.FirstBarStart.XFeet,
            placement.FirstBarEnd.YFeet - placement.FirstBarStart.YFeet,
            placement.FirstBarEnd.ZFeet - placement.FirstBarStart.ZFeet)
            .Normalize();
    }

    private static IsoFieldRebarPoint3D ShiftAlong(
        IsoFieldRebarPoint3D point,
        Vector direction,
        double distance)
    {
        return new IsoFieldRebarPoint3D(
            point.XFeet + (direction.X * distance),
            point.YFeet + (direction.Y * distance),
            point.ZFeet + (direction.Z * distance));
    }

    private static string RemoveBarIndex(string stableId)
    {
        int markerIndex = stableId.LastIndexOf(":b", StringComparison.Ordinal);
        return markerIndex < 0
            ? stableId
            : stableId.Substring(0, markerIndex);
    }

    private static bool MustReverse(Vector direction)
    {
        if (Math.Abs(direction.X) > DirectionTolerance)
        {
            return direction.X < 0;
        }

        if (Math.Abs(direction.Y) > DirectionTolerance)
        {
            return direction.Y < 0;
        }

        return direction.Z < 0;
    }

    private static long Quantize(double value, double tolerance)
    {
        return checked((long)Math.Round(value / tolerance, MidpointRounding.AwayFromZero));
    }

    private static IsoFieldRebarPoint3D Midpoint(
        IsoFieldRebarPoint3D first,
        IsoFieldRebarPoint3D second)
    {
        return new IsoFieldRebarPoint3D(
            (first.XFeet + second.XFeet) / 2,
            (first.YFeet + second.YFeet) / 2,
            (first.ZFeet + second.ZFeet) / 2);
    }

    private static Vector Subtract(IsoFieldRebarPoint3D first, IsoFieldRebarPoint3D second)
    {
        return new Vector(
            first.XFeet - second.XFeet,
            first.YFeet - second.YFeet,
            first.ZFeet - second.ZFeet);
    }

    private static Vector ToVector(IsoFieldRebarPoint3D point)
    {
        return new Vector(point.XFeet, point.YFeet, point.ZFeet);
    }

    private static double Dot(IsoFieldRebarPoint3D point, Vector vector)
    {
        return (point.XFeet * vector.X)
            + (point.YFeet * vector.Y)
            + (point.ZFeet * vector.Z);
    }

    private sealed record PreparedPlacement(
        IsoFieldRebarPlacement Source,
        IsoFieldRebarPoint3D Start,
        IsoFieldRebarPoint3D End,
        IsoFieldRebarPoint3D Midpoint,
        Vector Direction,
        Vector Normal,
        Vector CrossDirection,
        double Length);

    private sealed record GroupKey(
        string ZoneId,
        IsoFieldLayerRole? LayerRole,
        IsoFieldRebarFace? Face,
        string PlacementDirection,
        double DiameterMillimeters,
        double SpacingMillimeters,
        int CombinationIndex,
        int CombinationCount,
        long DirectionX,
        long DirectionY,
        long DirectionZ,
        long Length,
        long AlongCenter,
        long Plane);

    private sealed record SingletonRepairKey(
        string ZoneId,
        IsoFieldLayerRole? LayerRole,
        IsoFieldRebarFace? Face,
        string PlacementDirection,
        double DiameterMillimeters,
        double SpacingMillimeters,
        int CombinationIndex,
        int CombinationCount,
        long DirectionX,
        long DirectionY,
        long DirectionZ,
        long Plane);

    private sealed record FamilyInstanceKey(
        IsoFieldRebarFace? Face,
        string PlacementDirection,
        double DiameterMillimeters,
        double SpacingMillimeters,
        long FirstStartX,
        long FirstStartY,
        long FirstStartZ,
        long DirectionX,
        long DirectionY,
        long DirectionZ);

    private sealed record Vector(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public Vector Scale(double scale) => new(X * scale, Y * scale, Z * scale);

        public Vector Normalize()
        {
            double length = Length;
            if (length <= 1e-12)
            {
                throw new InvalidOperationException("Не удалось определить направление семейства армирования.");
            }

            return Scale(1 / length);
        }

        public Vector Cross(Vector other)
        {
            return new Vector(
                (Y * other.Z) - (Z * other.Y),
                (Z * other.X) - (X * other.Z),
                (X * other.Y) - (Y * other.X));
        }
    }
}
