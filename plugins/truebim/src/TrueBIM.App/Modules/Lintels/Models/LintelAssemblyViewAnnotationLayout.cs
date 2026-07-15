namespace TrueBIM.App.Modules.Lintels.Models;

public sealed class LintelViewProjectedBounds
{
    public LintelViewProjectedBounds(double minHorizontal, double maxHorizontal, double minVertical, double maxVertical)
    {
        MinHorizontal = Math.Min(minHorizontal, maxHorizontal);
        MaxHorizontal = Math.Max(minHorizontal, maxHorizontal);
        MinVertical = Math.Min(minVertical, maxVertical);
        MaxVertical = Math.Max(minVertical, maxVertical);
    }

    public double MinHorizontal { get; }

    public double MaxHorizontal { get; }

    public double MinVertical { get; }

    public double MaxVertical { get; }

    public double Width => MaxHorizontal - MinHorizontal;

    public double Height => MaxVertical - MinVertical;

    public double CenterHorizontal => (MinHorizontal + MaxHorizontal) * 0.5;

    public double CenterVertical => (MinVertical + MaxVertical) * 0.5;
}

public readonly record struct LintelViewPoint(double Horizontal, double Vertical);

public readonly record struct LintelViewSegment(LintelViewPoint Start, LintelViewPoint End);

public sealed class LintelAssemblyViewAnnotationLayout
{
    private const double FeetPerMillimeter = 1.0 / 304.8;

    private LintelAssemblyViewAnnotationLayout(
        double dimensionStart,
        double dimensionEnd,
        double dimensionVertical,
        double frameCenterHorizontal,
        double frameCenterVertical,
        double frameMinHorizontal,
        double frameMaxHorizontal,
        double frameMinVertical,
        double frameMaxVertical)
    {
        DimensionStart = dimensionStart;
        DimensionEnd = dimensionEnd;
        DimensionVertical = dimensionVertical;
        FrameCenterHorizontal = frameCenterHorizontal;
        FrameCenterVertical = frameCenterVertical;
        FrameMinHorizontal = frameMinHorizontal;
        FrameMaxHorizontal = frameMaxHorizontal;
        FrameMinVertical = frameMinVertical;
        FrameMaxVertical = frameMaxVertical;
    }

    public double DimensionStart { get; }

    public double DimensionEnd { get; }

    public double DimensionVertical { get; }

    public double FrameCenterHorizontal { get; }

    public double FrameCenterVertical { get; }

    public double FrameMinHorizontal { get; }

    public double FrameMaxHorizontal { get; }

    public double FrameMinVertical { get; }

    public double FrameMaxVertical { get; }

    public double FrameWidth => FrameMaxHorizontal - FrameMinHorizontal;

    public double FrameHeight => FrameMaxVertical - FrameMinVertical;

    public IReadOnlyList<LintelViewSegment> CreateFrameSegments()
    {
        LintelViewPoint bottomLeft = new(FrameMinHorizontal, FrameMinVertical);
        LintelViewPoint bottomRight = new(FrameMaxHorizontal, FrameMinVertical);
        LintelViewPoint topRight = new(FrameMaxHorizontal, FrameMaxVertical);
        LintelViewPoint topLeft = new(FrameMinHorizontal, FrameMaxVertical);
        return
        [
            new LintelViewSegment(bottomLeft, bottomRight),
            new LintelViewSegment(bottomRight, topRight),
            new LintelViewSegment(topRight, topLeft),
            new LintelViewSegment(topLeft, bottomLeft)
        ];
    }

    public static LintelAssemblyViewAnnotationLayout Create(
        LintelViewProjectedBounds bounds,
        int viewScale,
        double minimumFrameWidthMm = 1050,
        double minimumFrameHeightMm = 385,
        double dimensionOffsetOnPaperMm = 5,
        double horizontalGeometryMarginMm = 80,
        double verticalGeometryMarginMm = 50)
    {
        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }

        double scale = Math.Max(1, viewScale);
        double dimensionOffset = Math.Max(0, dimensionOffsetOnPaperMm) * scale * FeetPerMillimeter;
        double horizontalMargin = Math.Max(0, horizontalGeometryMarginMm) * FeetPerMillimeter;
        double verticalMargin = Math.Max(0, verticalGeometryMarginMm) * FeetPerMillimeter;
        double minimumFrameWidth = Math.Max(1, minimumFrameWidthMm) * FeetPerMillimeter;
        double minimumFrameHeight = Math.Max(1, minimumFrameHeightMm) * FeetPerMillimeter;
        double requiredHeight = bounds.Height + dimensionOffset + (2 * verticalMargin);
        double frameWidth = Math.Max(minimumFrameWidth, bounds.Width + (2 * horizontalMargin));
        double frameHeight = Math.Max(minimumFrameHeight, requiredHeight);
        double centerHorizontal = bounds.CenterHorizontal;
        double centerVertical = bounds.CenterVertical - (dimensionOffset * 0.25);

        return new LintelAssemblyViewAnnotationLayout(
            bounds.MinHorizontal,
            bounds.MaxHorizontal,
            bounds.MinVertical - dimensionOffset,
            centerHorizontal,
            centerVertical,
            centerHorizontal - (frameWidth * 0.5),
            centerHorizontal + (frameWidth * 0.5),
            centerVertical - (frameHeight * 0.5),
            centerVertical + (frameHeight * 0.5));
    }
}
