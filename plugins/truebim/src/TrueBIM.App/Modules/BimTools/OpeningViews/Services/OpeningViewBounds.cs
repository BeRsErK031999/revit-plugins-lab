namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewBounds
{
    public OpeningViewBounds(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
    {
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    public double MinX { get; }

    public double MinY { get; }

    public double MinZ { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    public double MaxZ { get; }
}

public sealed class OpeningViewBoundsResult
{
    public OpeningViewBoundsResult(OpeningViewBounds bounds, bool usedViewSpecificFallback)
    {
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        UsedViewSpecificFallback = usedViewSpecificFallback;
    }

    public OpeningViewBounds Bounds { get; }

    public bool UsedViewSpecificFallback { get; }
}
