#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using static TrueBIM.App.Modules.SlabReinforcement.SlabGeometry;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal static class SlabBands
{
    // Revit accepts one closed curve loop per area. Split parallel to the bars, strictly between
    // stations, so no straight bar acquires a splice when an opening separates two areas.
    internal static IEnumerable<(IList<Curve> Outline, double[] Rows)> Profiles(SlabGeometry g, XYZ cross, double[] stations, double cover)
    {
        var loops = g.Loops.OrderByDescending(loop => Area(loop, g.N)).ToList();
        var shell = Offset(loops[0], g.N, cover, true);
        var holes = loops.Skip(1).Select(loop => Offset(loop, g.N, cover, false)).ToList();
        Solid Extrude(CurveLoop loop) => GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, g.N, 1);
        var solid = Extrude(shell);
        foreach (var hole in holes)
            solid = BooleanOperationsUtils.ExecuteBooleanOperation(solid, Extrude(hole), BooleanOperationsType.Difference);
        var section = g.Range(cross);
        var cuts = new List<double>
        {
            section.Min - 1
        };
        // Pick a line inside every void in the covered prism, away from bar stations.
        // One cut can open a number of voids at once.
        var ranges = Faces(solid, g.N).SelectMany(face => face.GetEdgesAsCurveLoops().OrderByDescending(loop => Area(loop, g.N)).Skip(1)).Select(loop => g.Range(cross, loop)).OrderBy(range => range.Max).ToList();
        foreach (var range in ranges)
        {
            if (cuts.Any(v => v > range.Min + Tolerance && v < range.Max - Tolerance))
                continue;
            var limits = stations.Where(v => v > range.Min && v < range.Max).Prepend(range.Min).Append(range.Max).ToArray();
            var gap = limits.Zip(limits.Skip(1), (left, right) => (left, right)).OrderByDescending(pair => pair.right - pair.left).First();
            cuts.Add((gap.left + gap.right) / 2);
        }

        cuts.Add(section.Max + 1);
        cuts.Sort();
        for (int i = 1; i < cuts.Count; i++)
        {
            double low = cuts[i - 1], high = cuts[i];
            if (!stations.Any(s => s > low && s < high))
                continue;
            var band = BooleanOperationsUtils.CutWithHalfSpace(solid, Plane.CreateByNormalAndOrigin(cross, g.Origin + cross * low));
            band = BooleanOperationsUtils.CutWithHalfSpace(band, Plane.CreateByNormalAndOrigin(-cross, g.Origin + cross * high));
            foreach (var face in SolidUtils.SplitVolumes(band).SelectMany(part => Faces(part, g.N)))
            {
                var profiles = face.GetEdgesAsCurveLoops();
                if (profiles.Count != 1)
                    throw new InvalidOperationException($"Не удалось раскрыть вырез в полосе {low / Mm:F0}…{high / Mm:F0} мм: {string.Join(", ", profiles.Select(p => $"{p.IsCounterclockwise(g.N)} {g.Range(cross, p).Min / Mm:F0}…{g.Range(cross, p).Max / Mm:F0}"))}.");
                var loop = profiles[0];
                var extent = g.Range(cross, loop);
                var rows = stations.Where(s => s >= extent.Min - Tolerance && s <= extent.Max + Tolerance).ToArray();
                if (rows.Length == 0)
                    continue;
                var down = Transform.CreateTranslation(-g.N);
                yield return (loop.Select(c => c.CreateTransformed(down)).ToList(), rows);
            }
        }
    }

    private static IEnumerable<PlanarFace> Faces(Solid solid, XYZ normal) => solid.Faces.OfType<PlanarFace>().Where(face => face.FaceNormal.DotProduct(normal) > 0.999999);
    private static double Area(CurveLoop loop, XYZ normal) => GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, normal, 1).Volume;
    private static CurveLoop Offset(CurveLoop loop, XYZ normal, double cover, bool outer)
    {
        double sign = loop.IsCounterclockwise(normal) == outer ? -1 : 1;
        try
        {
            return CurveLoop.CreateViaOffset(loop, sign * cover, normal);
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            throw new InvalidOperationException("Контур плиты не допускает заданный защитный слой. Проверьте радиусы и узкие вырезы.");
        }
    }
}
#endif
