namespace TrueBIM.App.Modules.RebarDetailing;

public static class DetailingBoundsPlan
{
    public static DetailingRect Around(DetailingRect concrete, DetailingRect bars, double clearance) => new(
        Math.Min(concrete.Left, bars.Left) - clearance, Math.Min(concrete.Bottom, bars.Bottom) - clearance,
        Math.Max(concrete.Right, bars.Right) + clearance, Math.Max(concrete.Top, bars.Top) + clearance);

    public static DetailingRect Expand(DetailingRect crop, DetailingRect graphics, double margin) => new(
        Math.Min(crop.Left, graphics.Left - margin), Math.Min(crop.Bottom, graphics.Bottom - margin),
        Math.Max(crop.Right, graphics.Right + margin), Math.Max(crop.Top, graphics.Top + margin));
}
