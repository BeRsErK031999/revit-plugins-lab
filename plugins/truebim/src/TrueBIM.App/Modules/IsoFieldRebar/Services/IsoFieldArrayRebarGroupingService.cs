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

        return prepared
            .GroupBy(BuildGroupKey)
            .Select(CreateZoneArray)
            .OrderBy(item => item.StableId, StringComparer.Ordinal)
            .ToArray();
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
        return new PreparedPlacement(
            placement,
            start,
            end,
            direction,
            normal,
            crossDirection);
    }

    private static ZoneArrayKey BuildGroupKey(PreparedPlacement item)
    {
        IsoFieldRebarComponent component = item.Source.Component!;
        return new ZoneArrayKey(
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
            Quantize(item.Normal.X, DirectionTolerance),
            Quantize(item.Normal.Y, DirectionTolerance),
            Quantize(item.Normal.Z, DirectionTolerance),
            Quantize(Dot(item.Start, item.Normal), GeometryToleranceFeet));
    }

    private static IsoFieldArrayRebarPlacement CreateZoneArray(
        IGrouping<ZoneArrayKey, PreparedPlacement> group)
    {
        PreparedPlacement[] items = group
            .OrderBy(item => item.Source.StableId, StringComparer.Ordinal)
            .ToArray();
        PreparedPlacement first = items[0];
        Vector direction = first.Direction;
        Vector crossDirection = first.CrossDirection;
        IEnumerable<IsoFieldRebarPoint3D> points = items
            .SelectMany(item => new[] { item.Start, item.End });
        double minimumAlong = points.Min(point => Dot(point, direction));
        double maximumAlong = points.Max(point => Dot(point, direction));
        double minimumCross = points.Min(point => Dot(point, crossDirection));
        double maximumCross = points.Max(point => Dot(point, crossDirection));
        double length = maximumAlong - minimumAlong;
        double width = maximumCross - minimumCross;

        IsoFieldRebarPoint3D origin = ShiftAlong(
            first.Start,
            direction,
            minimumAlong - Dot(first.Start, direction));
        origin = ShiftAlong(
            origin,
            crossDirection,
            minimumCross - Dot(origin, crossDirection));
        IsoFieldRebarPoint3D end = ShiftAlong(origin, direction, length);
        IsoFieldRebarPoint3D lastStart = ShiftAlong(origin, crossDirection, width);
        string[] sourceStableIds = items
            .Select(item => item.Source.StableId!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new IsoFieldArrayRebarPlacement(
            first.Source.ZoneId,
            first.Source.ZoneName,
            first.Source.Rule,
            first.Source.Component!,
            origin,
            end,
            lastStart,
            first.Source.Normal,
            ResolveBarCount(width, first.Source.Component!.SpacingMillimeters),
            BuildZoneArrayStableId(first.Source.StableId!),
            sourceStableIds);
    }

    private static int ResolveBarCount(double widthFeet, double spacingMillimeters)
    {
        double spacingFeet = spacingMillimeters / MillimetersPerFoot;
        return Math.Max(
            1,
            checked((int)Math.Floor((widthFeet + GeometryToleranceFeet) / spacingFeet) + 1));
    }

    private static string BuildZoneArrayStableId(string sourceStableId)
    {
        int regionMarkerIndex = sourceStableId.LastIndexOf(":r", StringComparison.Ordinal);
        return regionMarkerIndex < 0
            ? sourceStableId + ":r0:a0"
            : sourceStableId.Substring(0, regionMarkerIndex) + ":r0:a0";
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

    private static long Quantize(double value, double tolerance)
    {
        return checked((long)Math.Round(value / tolerance, MidpointRounding.AwayFromZero));
    }

    private sealed record PreparedPlacement(
        IsoFieldRebarPlacement Source,
        IsoFieldRebarPoint3D Start,
        IsoFieldRebarPoint3D End,
        Vector Direction,
        Vector Normal,
        Vector CrossDirection);

    private sealed record ZoneArrayKey(
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
        long NormalX,
        long NormalY,
        long NormalZ,
        long Plane);

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
