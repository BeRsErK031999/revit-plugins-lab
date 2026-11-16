using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed partial class RebarDetailingService
{
    private sealed class BundleTarget
    {
        public DetailingBar Bar { get; set; } = null!;
        public XYZ Point { get; set; } = null!;
        public Reference Reference { get; set; } = null!;
    }

    private static double BarDiameter(DetailingBar bar)
    {
        var type = bar.Element.Document.GetElement(bar.Element.GetTypeId());
        double diameter = type?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER)?.AsDouble() ?? 0;
#if REVIT2023_OR_GREATER
        diameter = type?.get_Parameter(BuiltInParameter.REBAR_MODEL_BAR_DIAMETER)?.AsDouble() ?? diameter;
#endif
        return diameter * 304.8;
    }

    private static List<List<BundleTarget>> TransverseBundles(DetailingFrame frame, IEnumerable<DetailingBar> bars)
    {
        var points = bars.Where(b => !IsPin(b) && !Longitudinal(frame, b))
            .SelectMany(b => b.Positions.Select((point, index) => new BundleTarget
                { Bar = b, Point = point, Reference = b.PositionReferences[index] })).ToList();
        return DetailingBarBundles.Create(points.Select((p, i) => (i, frame.X(p.Point) * 304.8,
            frame.Y(p.Point) * 304.8, BarDiameter(p.Bar))))
            .Select(group => group.Select(i => points[i]).ToList()).ToList();
    }

    private static bool HasBundle(DetailingFrame frame, IEnumerable<List<BundleTarget>> bundles) =>
        bundles.Any(group => group.Count > 1 && frame.Y(group.Last().Point) - frame.Y(group[0].Point) > 0.1 / 304.8);
}
