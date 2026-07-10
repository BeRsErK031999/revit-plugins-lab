namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed class OpeningViewProjectedBounds
{
    public OpeningViewProjectedBounds(double minHorizontal, double maxHorizontal, double minVertical, double maxVertical)
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

    public double CenterHorizontal => (MinHorizontal + MaxHorizontal) * 0.5;
}

public sealed class OpeningViewAnnotationLayout
{
    private const double FeetPerMillimeter = 1.0 / 304.8;

    private OpeningViewAnnotationLayout(
        double horizontalStart,
        double horizontalEnd,
        double horizontalPosition,
        double verticalPosition,
        double verticalStart,
        double verticalEnd,
        double titleHorizontal,
        double titleVertical,
        double requiredMinHorizontal,
        double requiredMaxHorizontal,
        double requiredMinVertical,
        double requiredMaxVertical)
    {
        HorizontalStart = horizontalStart;
        HorizontalEnd = horizontalEnd;
        HorizontalPosition = horizontalPosition;
        VerticalPosition = verticalPosition;
        VerticalStart = verticalStart;
        VerticalEnd = verticalEnd;
        TitleHorizontal = titleHorizontal;
        TitleVertical = titleVertical;
        RequiredMinHorizontal = requiredMinHorizontal;
        RequiredMaxHorizontal = requiredMaxHorizontal;
        RequiredMinVertical = requiredMinVertical;
        RequiredMaxVertical = requiredMaxVertical;
    }

    public double HorizontalStart { get; }

    public double HorizontalEnd { get; }

    public double HorizontalPosition { get; }

    public double VerticalPosition { get; }

    public double VerticalStart { get; }

    public double VerticalEnd { get; }

    public double TitleHorizontal { get; }

    public double TitleVertical { get; }

    public double RequiredMinHorizontal { get; }

    public double RequiredMaxHorizontal { get; }

    public double RequiredMinVertical { get; }

    public double RequiredMaxVertical { get; }

    public static OpeningViewAnnotationLayout Create(
        OpeningViewProjectedBounds bounds,
        int viewScale,
        double dimensionOffsetMm = 4,
        double titleOffsetMm = 6,
        double cropPaddingMm = 2)
    {
        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }

        double scale = Math.Max(1, viewScale);
        double dimensionOffset = Math.Max(0, dimensionOffsetMm) * scale * FeetPerMillimeter;
        double titleOffset = Math.Max(0, titleOffsetMm) * scale * FeetPerMillimeter;
        double cropPadding = Math.Max(0, cropPaddingMm) * scale * FeetPerMillimeter;

        return new OpeningViewAnnotationLayout(
            bounds.MinHorizontal,
            bounds.MaxHorizontal,
            bounds.MinVertical - dimensionOffset,
            bounds.MaxHorizontal + dimensionOffset,
            bounds.MinVertical,
            bounds.MaxVertical,
            bounds.CenterHorizontal,
            bounds.MaxVertical + titleOffset,
            bounds.MinHorizontal - cropPadding,
            bounds.MaxHorizontal + dimensionOffset + cropPadding,
            bounds.MinVertical - dimensionOffset - cropPadding,
            bounds.MaxVertical + titleOffset + cropPadding);
    }
}
