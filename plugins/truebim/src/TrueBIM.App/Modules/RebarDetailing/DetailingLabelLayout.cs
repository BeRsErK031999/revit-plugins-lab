namespace TrueBIM.App.Modules.RebarDetailing;

public enum DetailingLabelSide { Left, Right, Above, Below }

public readonly struct DetailingRect
{
    public double Left { get; }
    public double Bottom { get; }
    public double Right { get; }
    public double Top { get; }
    public double Width => Right - Left;
    public double Height => Top - Bottom;
    public double CenterX => (Left + Right) / 2;
    public double CenterY => (Bottom + Top) / 2;
    public DetailingRect(double left, double bottom, double right, double top)
    { Left = left; Bottom = bottom; Right = right; Top = top; }
    public bool Intersects(DetailingRect other, double margin = 0) => Left < other.Right + margin
        && Right > other.Left - margin && Bottom < other.Top + margin && Top > other.Bottom - margin;
}

public sealed class DetailingLabelRequest
{
    public int Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public DetailingRect Host { get; set; }
    public DetailingLabelSide PreferredSide { get; set; }
    public bool OnlyPreferredSide { get; set; }
    public double MinX { get; set; } = double.NegativeInfinity;
    public double MaxX { get; set; } = double.PositiveInfinity;
    public double MinY { get; set; } = double.NegativeInfinity;
    public double MaxY { get; set; } = double.PositiveInfinity;
    public double? PreferredX { get; set; }
    public double? PreferredY { get; set; }
    public List<(double X, double Y)> Targets { get; } = new();
    public bool SlidingTarget { get; set; }
    public bool SlideAlongX { get; set; }
    public double SlideMin { get; set; }
    public double SlideMax { get; set; }
    public double SlideFixed { get; set; }
}

public sealed class DetailingLabelPlacement
{
    public DetailingLabelRequest Request { get; }
    public DetailingRect Bounds { get; }
    public DetailingLabelSide Side { get; }
    public bool Crowded { get; }
    public double EndX => Side == DetailingLabelSide.Left ? Bounds.Right : Side == DetailingLabelSide.Right ? Bounds.Left
        : Request.X >= Bounds.CenterX ? Bounds.Right : Bounds.Left;
    public double EndY => Bounds.CenterY;
    public DetailingLabelPlacement(DetailingLabelRequest request, DetailingRect bounds, DetailingLabelSide side, bool crowded)
    { Request = request; Bounds = bounds; Side = side; Crowded = crowded; }
}

/// <summary>Places label heads in paper millimetres; leader extents never become label obstacles.</summary>
public static class DetailingLabelLayout
{
    public static IReadOnlyList<DetailingLabelPlacement> Place(IEnumerable<DetailingLabelRequest> requests,
        IEnumerable<DetailingRect> obstacles, double gap = 8, double clearance = 2,
        IEnumerable<DetailingRect>? leaderObstacles = null)
    {
        var fixedObstacles = obstacles.ToList();
        var textObstacles = leaderObstacles?.ToList() ?? new List<DetailingRect>();
        var items = requests.ToList();
        var standard = items.OrderBy(r => r.PreferredSide == DetailingLabelSide.Above ? 1 : 0)
            .ThenBy(r => r.PreferredSide == DetailingLabelSide.Above ? r.Width : 0).ThenByDescending(r => r.Y).ThenBy(r => r.X).ThenBy(r => r.Id);
        var orders = new IEnumerable<DetailingLabelRequest>[] { standard,
            items.OrderBy(r => r.PreferredSide == DetailingLabelSide.Above ? 0 : 1).ThenByDescending(r => r.Width).ThenBy(r => r.Y),
            items.OrderBy(r => r.Y).ThenByDescending(r => r.Width),
            items.OrderByDescending(r => r.Width).ThenBy(r => r.Y), standard.Reverse() };
        const double leaderPenalty = 10000;
        return orders.Select(Solve).OrderBy(Score).First();

        List<DetailingLabelPlacement> Solve(IEnumerable<DetailingLabelRequest> order)
        {
            List<DetailingLabelPlacement> placements = new();
            foreach (DetailingLabelRequest request in order) placements.Add(Choose(request, placements));
            // Revisit earlier heads after the whole group is known. Try several
            // deterministic orders so a wide caption does not trap smaller heads.
            for (int pass = 0; pass < 4; pass++)
            {
            for (int index = 0; index < placements.Count; index++)
                placements[index] = Choose(placements[index].Request, placements.Where((_, i) => i != index).ToList(), placements[index]);
                // Two stacked heads may need to exchange rows together. Moving
                // either one alone would temporarily collide with its neighbour.
                for (int i = 0; i < placements.Count; i++)
                for (int j = i + 1; j < placements.Count; j++)
                {
                    var first = placements[i]; var second = placements[j];
                    double shift = second.Bounds.CenterY - first.Bounds.CenterY;
                    var a = Shift(first, shift); var b = Shift(second, -shift);
                    if (!Allowed(a) || !Allowed(b)) continue;
                    double previous = Score(placements);
                    placements[i] = a; placements[j] = b;
                    if (Score(placements) >= previous - 0.01) { placements[i] = first; placements[j] = second; }
                }
            }
            return placements;
        }

        DetailingLabelPlacement Shift(DetailingLabelPlacement label, double y) => new(label.Request,
            new DetailingRect(label.Bounds.Left, label.Bounds.Bottom + y, label.Bounds.Right, label.Bounds.Top + y), label.Side, label.Crowded);

        bool Allowed(DetailingLabelPlacement label) => label.Bounds.Bottom >= label.Request.MinY
            && label.Bounds.Top <= label.Request.MaxY
            && (label.Side != DetailingLabelSide.Above || label.Bounds.Bottom >= label.Request.Host.Top + gap)
            && (label.Side != DetailingLabelSide.Below || label.Bounds.Top <= label.Request.Host.Bottom - gap);

        double Score(List<DetailingLabelPlacement> placements)
        {
            double score = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                var label = placements[i];
                score += Distance(label) + fixedObstacles.Count(o => o.Intersects(label.Bounds, clearance)) * 1e8
                    + textObstacles.Count(o => Hits(label, o)) * leaderPenalty;
                foreach (var other in placements.Skip(i + 1))
                    score += (label.Bounds.Intersects(other.Bounds, clearance) ? 1e8 : 0)
                        + (Hits(label, other.Bounds) || Hits(other, label.Bounds) ? leaderPenalty : 0);
            }
            return score;
        }

        double Distance(DetailingLabelPlacement label)
        {
            double dx = label.Bounds.CenterX - (label.Request.PreferredX ?? label.Request.X);
            double dy = label.Bounds.CenterY - (label.Request.PreferredY ?? label.Request.Y);
            return dx * dx + dy * dy + (label.Side == label.Request.PreferredSide ? 0 : 225);
        }

        DetailingLabelPlacement Choose(DetailingLabelRequest request, IReadOnlyList<DetailingLabelPlacement> peers, DetailingLabelPlacement? current = null)
        {
            var pool = Candidates(request, gap, clearance, fixedObstacles.Concat(peers.Select(p => p.Bounds)));
            if (current != null) pool = new[] { current }.Concat(pool);
            var candidates = pool.Select(candidate =>
            {
                int collisions = fixedObstacles.Count(o => o.Intersects(candidate.Bounds, clearance))
                    + peers.Count(p => p.Bounds.Intersects(candidate.Bounds, clearance));
                int leaderHits = peers.Count(p => Hits(candidate, p.Bounds) || Hits(p, candidate.Bounds))
                    + textObstacles.Count(o => Hits(candidate, o));
                return new { Candidate = candidate, Collisions = collisions, Score = collisions * 1e8 + leaderHits * leaderPenalty + Distance(candidate) };
            }).OrderBy(candidate => candidate.Score).First();
            return new DetailingLabelPlacement(request, candidates.Candidate.Bounds, candidates.Candidate.Side, candidates.Collisions > 0);
        }

        bool Hits(DetailingLabelPlacement label, DetailingRect box)
        {
            DetailingLabelRequest request = label.Request;
            if (request.SlidingTarget)
            {
                double position = Math.Max(request.SlideMin, Math.Min(request.SlideMax,
                    request.SlideAlongX ? label.Bounds.CenterX : label.Bounds.CenterY));
                return IntersectsSegment(box, request.SlideAlongX ? position : request.SlideFixed,
                    request.SlideAlongX ? request.SlideFixed : position, label.EndX, label.EndY);
            }
            return request.Targets.Count == 0
                ? IntersectsSegment(box, request.X, request.Y, label.EndX, label.EndY)
                : request.Targets.Any(t => IntersectsSegment(box, t.X, t.Y, label.EndX, label.EndY));
        }
    }

    private static IEnumerable<DetailingLabelPlacement> Candidates(DetailingLabelRequest request, double gap, double clearance, IEnumerable<DetailingRect> obstacles)
    {
        var sides = request.OnlyPreferredSide ? new[] { request.PreferredSide }
            : new[] { request.PreferredSide, DetailingLabelSide.Left, DetailingLabelSide.Right, DetailingLabelSide.Above, DetailingLabelSide.Below }.Distinct();
        double row = request.Height + 3;
        foreach (DetailingLabelSide side in sides)
        {
            bool vertical = side == DetailingLabelSide.Left || side == DetailingLabelSide.Right;
            double size = vertical ? request.Height : request.Width;
            double start = vertical ? (request.PreferredY ?? request.Y) - request.Height / 2 : (request.PreferredX ?? request.X) - request.Width / 2;
            var offsets = new[] { 0.0, row, -row, 2 * row, -2 * row, 3 * row, -3 * row, 4 * row, -4 * row }
                .Concat(obstacles.SelectMany(o => new[] { (vertical ? o.Top : o.Right) + clearance + 0.1 - start,
                    (vertical ? o.Bottom : o.Left) - clearance - size - 0.1 - start })).Distinct();
            double baseX = side == DetailingLabelSide.Left ? request.Host.Left - gap - request.Width : request.Host.Right + gap;
            var depths = new[] { 0.0, row, 2 * row, 3 * row, 4 * row }.Concat(vertical
                ? obstacles.SelectMany(o => new[] { side == DetailingLabelSide.Left ? baseX - (o.Left - clearance - request.Width - 0.1) : o.Right + clearance + 0.1 - baseX })
                : obstacles.Select(o => side == DetailingLabelSide.Above
                    ? o.Top + clearance + 0.1 - (request.Host.Top + gap)
                    : request.Host.Bottom - gap - (o.Bottom - clearance - 0.1))).Where(d => d >= 0).Distinct();
            foreach (double offset in offsets)
            foreach (double depth in depths)
            {
                double x, y;
                if (side == DetailingLabelSide.Left || side == DetailingLabelSide.Right)
                {
                    x = side == DetailingLabelSide.Left ? request.Host.Left - gap - request.Width - depth : request.Host.Right + gap + depth;
                    x = Math.Min(request.MaxX - request.Width, Math.Max(request.MinX, x));
                    y = (request.PreferredY ?? request.Y) - request.Height / 2 + offset;
                }
                else
                {
                    x = Math.Min(request.MaxX - request.Width, Math.Max(request.MinX, (request.PreferredX ?? request.X) - request.Width / 2 + offset));
                    y = side == DetailingLabelSide.Below ? request.Host.Bottom - gap - request.Height - depth : request.Host.Top + gap + depth;
                }
                y = Math.Min(request.MaxY - request.Height, Math.Max(request.MinY, y));
                yield return new DetailingLabelPlacement(request, new DetailingRect(x, y, x + request.Width, y + request.Height), side, false);
            }
        }
    }

    private static bool IntersectsSegment(DetailingRect box, double x0, double y0, double x1, double y1)
    {
        double low = 0, high = 1;
        foreach (var axis in new[] { (Start: x0, Delta: x1 - x0, Min: box.Left, Max: box.Right), (Start: y0, Delta: y1 - y0, Min: box.Bottom, Max: box.Top) })
        {
            if (Math.Abs(axis.Delta) < 1e-9) { if (axis.Start < axis.Min || axis.Start > axis.Max) return false; continue; }
            double a = (axis.Min - axis.Start) / axis.Delta, b = (axis.Max - axis.Start) / axis.Delta;
            low = Math.Max(low, Math.Min(a, b)); high = Math.Min(high, Math.Max(a, b));
            if (low > high) return false;
        }
        return true;
    }
}
