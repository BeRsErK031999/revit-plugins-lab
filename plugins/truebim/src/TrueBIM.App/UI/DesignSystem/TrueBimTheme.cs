using System.Windows;
using System.Windows.Media;

namespace TrueBIM.App.UI.DesignSystem;

public enum TrueBimUiSeverity
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger
}

public static class TrueBimTheme
{
    public static readonly FontFamily FontFamily = new("Segoe UI Variable Text, Segoe UI");

    public const double FontSize = 13;
    public const double CaptionFontSize = 11;
    public const double SectionTitleFontSize = 16;
    public const double WindowTitleFontSize = 22;

    public static readonly Color PrimaryColor = Color.FromRgb(30, 69, 148);
    public static readonly Color PrimaryHoverColor = Color.FromRgb(24, 58, 126);
    public static readonly Color PrimaryPressedColor = Color.FromRgb(18, 46, 102);
    public static readonly Color AccentColor = Color.FromRgb(45, 112, 177);
    public static readonly Color WindowBackgroundColor = Color.FromRgb(245, 247, 250);
    public static readonly Color SurfaceColor = Colors.White;
    public static readonly Color SurfaceAltColor = Color.FromRgb(238, 242, 247);
    public static readonly Color BorderColor = Color.FromRgb(210, 218, 229);
    public static readonly Color TextPrimaryColor = Color.FromRgb(24, 36, 51);
    public static readonly Color TextSecondaryColor = Color.FromRgb(72, 84, 99);
    public static readonly Color TextMutedColor = Color.FromRgb(112, 124, 140);
    public static readonly Color SuccessColor = Color.FromRgb(36, 130, 91);
    public static readonly Color WarningColor = Color.FromRgb(181, 117, 24);
    public static readonly Color DangerColor = Color.FromRgb(186, 53, 67);
    public static readonly Color InfoColor = Color.FromRgb(42, 112, 183);
    public static readonly Color DisabledColor = Color.FromRgb(158, 169, 183);

    public const double Spacing4 = 4;
    public const double Spacing8 = 8;
    public const double Spacing12 = 12;
    public const double Spacing16 = 16;
    public const double Spacing24 = 24;

    public const double Radius6 = 6;
    public const double Radius8 = 8;
    public const double Radius12 = 12;

    public const double BorderWidth = 1;
    public const double ControlHeight32 = 32;
    public const double ControlHeight36 = 36;
    public const double HeaderHeight = 64;
    public const double FooterHeight = 52;

    public const double IconSizeSmall = 18;
    public const double IconSizeCommand = 22;
    public const double IconSizeHeader = 28;
    public const double IconSizeRibbonSmall = 16;
    public const double IconSizeRibbon = 32;

    public static readonly Thickness WindowPadding = new(Spacing16);
    public static readonly Thickness SectionPadding = new(Spacing16);
    public static readonly Thickness ControlPadding = new(Spacing12, 0, Spacing12, 0);
    public static readonly Thickness CompactControlPadding = new(Spacing8, 0, Spacing8, 0);
    public static readonly Thickness BadgePadding = new(Spacing8, 2, Spacing8, 3);
}
