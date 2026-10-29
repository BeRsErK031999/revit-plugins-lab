using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarDetailing;

internal sealed class DetailingSection
{
    public string HostKey { get; set; } = "";
    public List<DetailingBar> Bars { get; set; } = new();
    public List<DetailingFace> Faces { get; set; } = new();
    public List<DetailingHost> UpperSlabs { get; set; } = new();
    public List<DetailingHost> LowerConcrete { get; set; } = new();
    public DetailingRect Wall { get; set; }
    public DetailingRect Reinforcement { get; set; }
    public bool LowerLeft { get; set; }
    public bool LowerRight { get; set; }
    public double BreakLeft { get; set; }
    public double BreakRight { get; set; }
    public double BreakBottom { get; set; }
}

public sealed partial class RebarDetailingService
{
    private static void ValidateVariant(DetailingFrame frame, DetailingGeometry geometry, DetailingOptions options)
    {
        var offsets = new List<double>();
        foreach (var bar in geometry.Bars.Where(b => !IsPin(b)))
        foreach (var position in bar.Segments.GroupBy(s => s.BarIndex))
        {
            var spine = position.Where(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999)
                .OrderByDescending(s => s.Start.DistanceTo(s.End)).FirstOrDefault();
            if (spine == null || spine.Start.DistanceTo(spine.End) < 500 / 304.8) continue;
            foreach (var bend in position.Where(s => Math.Abs(s.Direction.DotProduct(frame.Right)) > 0.999
                && s.Start.DistanceTo(s.End) > 50 / 304.8))
            {
                double y = frame.Y(bend.Start);
                if (Math.Min(Math.Abs(y - frame.Y(spine.Start)), Math.Abs(y - frame.Y(spine.End))) > 100 / 304.8) continue;
                double a = frame.X(bend.Start) - frame.X(spine.Start), b = frame.X(bend.End) - frame.X(spine.Start);
                offsets.Add((Math.Abs(a) > Math.Abs(b) ? a : b) * 304.8);
            }
        }
        DetailingVariant detected = DetailingSectionLayout.VariantForBends(offsets);
        if (detected == DetailingVariant.Straight || detected == options.Variant) return;
        string expected = detected == DetailingVariant.BentSameSide ? "Г-образный в одну сторону" : "Г-образный в разные стороны";
        throw new InvalidOperationException("Видимые загибы продольной арматуры не соответствуют выбранному варианту. Выберите «" + expected + "».");
    }

    private static List<DetailingSection> Sections(DetailingFrame frame, DetailingGeometry geometry, DetailingOptions options)
    {
        var sections = new List<DetailingSection>();
        foreach (var group in geometry.Bars.GroupBy(b => DetailingGeometry.Key(b.Host, b.Link)))
        {
            var bars = group.ToList();
            DetailingRect reinforcement = Region(frame, bars);
            var faces = geometry.Faces.Where(f => f.HostKey == group.Key).ToList();
            var sides = faces.Where(f => f.Horizontal).ToList();
            var levels = faces.Where(f => !f.Horizontal).ToList();
            var vertical = bars.SelectMany(b => b.Segments).Where(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999).ToList();
            double left = sides.Count >= 2 ? sides.Min(f => frame.X(f.Point)) : vertical.Count > 0 ? vertical.Min(s => frame.X(s.Start)) : reinforcement.Left;
            double right = sides.Count >= 2 ? sides.Max(f => frame.X(f.Point)) : vertical.Count > 0 ? vertical.Max(s => frame.X(s.Start)) : reinforcement.Right;
            double bottom = levels.Count >= 2 ? levels.Min(f => frame.Y(f.Point)) : reinforcement.Bottom;
            double top = levels.Count >= 2 ? levels.Max(f => frame.Y(f.Point)) : reinforcement.Top;
            var others = geometry.Hosts.Where(h => h.Key != group.Key && geometry.Faces.Any(f => f.HostKey == h.Key)
                && h.Bounds.Right >= left - 1 / 304.8 && h.Bounds.Left <= right + 1 / 304.8).ToList();
            var upper = others.Where(h => (h.Element is Floor || h.Element.Category?.Id == new ElementId(BuiltInCategory.OST_StructuralFoundation))
                && h.Bounds.Top > top + 1 / 304.8 && h.Bounds.Bottom <= top + 1 / 304.8
                && h.Bounds.Top <= top + 1000 / 304.8).ToList();
            var lower = others.Where(h => h.Bounds.Bottom < bottom - 1 / 304.8 && h.Bounds.Top >= bottom - 1 / 304.8
                && h.Bounds.Top < bottom + frame.Paper(6)).ToList();
            bool lowerLeft = lower.Any(h => h.Bounds.Left < left - 1 / 304.8);
            bool lowerRight = lower.Any(h => h.Bounds.Right > right + 1 / 304.8);
            double outerLeft = left, outerRight = right;
            if (options.Variant != DetailingVariant.Straight)
            {
                // The bend envelope also covers mirrored sections. Only the
                // longitudinal bars extend these margins, never unrelated ties.
                var longitudinal = bars.Where(b => b.Segments.Any(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999 && s.Start.DistanceTo(s.End) > 500 / 304.8)).ToList();
                if (longitudinal.Count > 0)
                {
                    DetailingRect bent = Region(frame, longitudinal);
                    outerLeft = Math.Min(left, bent.Left); outerRight = Math.Max(right, bent.Right);
                }
            }
            bool slabLeft = upper.Any(h => h.Bounds.Left < left - 1 / 304.8);
            bool slabRight = upper.Any(h => h.Bounds.Right > right + 1 / 304.8);
            sections.Add(new DetailingSection { HostKey = group.Key, Bars = bars, Faces = faces, UpperSlabs = upper, LowerConcrete = lower,
                Wall = new DetailingRect(left, bottom, right, top), Reinforcement = reinforcement, LowerLeft = lowerLeft, LowerRight = lowerRight,
                BreakLeft = outerLeft - (options.Details && (lowerLeft || slabLeft) ? frame.Paper(5) : 0),
                BreakRight = outerRight + (options.Details && (lowerRight || slabRight) ? frame.Paper(5) : 0),
                BreakBottom = options.Details ? Math.Min(bottom - frame.Paper(6), reinforcement.Bottom - frame.Paper(2)) : Math.Min(bottom, reinforcement.Bottom) });
        }
        return sections;
    }
}
