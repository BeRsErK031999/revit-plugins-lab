using System.Windows.Media;

namespace TrueBIM.App.UI.DesignSystem;

public static class TrueBimBrushes
{
    public static SolidColorBrush Primary { get; } = Create(TrueBimTheme.PrimaryColor);
    public static SolidColorBrush PrimaryHover { get; } = Create(TrueBimTheme.PrimaryHoverColor);
    public static SolidColorBrush PrimaryPressed { get; } = Create(TrueBimTheme.PrimaryPressedColor);
    public static SolidColorBrush Accent { get; } = Create(TrueBimTheme.AccentColor);
    public static SolidColorBrush WindowBackground { get; } = Create(TrueBimTheme.WindowBackgroundColor);
    public static SolidColorBrush Surface { get; } = Create(TrueBimTheme.SurfaceColor);
    public static SolidColorBrush SurfaceAlt { get; } = Create(TrueBimTheme.SurfaceAltColor);
    public static SolidColorBrush Border { get; } = Create(TrueBimTheme.BorderColor);
    public static SolidColorBrush TextPrimary { get; } = Create(TrueBimTheme.TextPrimaryColor);
    public static SolidColorBrush TextSecondary { get; } = Create(TrueBimTheme.TextSecondaryColor);
    public static SolidColorBrush TextMuted { get; } = Create(TrueBimTheme.TextMutedColor);
    public static SolidColorBrush Success { get; } = Create(TrueBimTheme.SuccessColor);
    public static SolidColorBrush Warning { get; } = Create(TrueBimTheme.WarningColor);
    public static SolidColorBrush Danger { get; } = Create(TrueBimTheme.DangerColor);
    public static SolidColorBrush Info { get; } = Create(TrueBimTheme.InfoColor);
    public static SolidColorBrush Disabled { get; } = Create(TrueBimTheme.DisabledColor);
    public static SolidColorBrush DisabledSurface { get; } = Create(Color.FromRgb(230, 235, 241));

    public static SolidColorBrush SuccessBackground { get; } = Create(Color.FromRgb(229, 245, 238));
    public static SolidColorBrush WarningBackground { get; } = Create(Color.FromRgb(255, 245, 222));
    public static SolidColorBrush DangerBackground { get; } = Create(Color.FromRgb(253, 235, 238));
    public static SolidColorBrush InfoBackground { get; } = Create(Color.FromRgb(230, 241, 252));
    public static SolidColorBrush NeutralBackground { get; } = Create(Color.FromRgb(238, 242, 247));

    public static SolidColorBrush ForSeverity(TrueBimUiSeverity severity)
    {
        return severity switch
        {
            TrueBimUiSeverity.Success => Success,
            TrueBimUiSeverity.Warning => Warning,
            TrueBimUiSeverity.Danger => Danger,
            TrueBimUiSeverity.Info => Info,
            _ => TextSecondary
        };
    }

    public static SolidColorBrush BackgroundForSeverity(TrueBimUiSeverity severity)
    {
        return severity switch
        {
            TrueBimUiSeverity.Success => SuccessBackground,
            TrueBimUiSeverity.Warning => WarningBackground,
            TrueBimUiSeverity.Danger => DangerBackground,
            TrueBimUiSeverity.Info => InfoBackground,
            _ => NeutralBackground
        };
    }

    private static SolidColorBrush Create(Color color)
    {
        SolidColorBrush brush = new(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
