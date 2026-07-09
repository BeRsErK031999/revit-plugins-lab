using System.Windows;
using System.Windows.Media;

namespace TrueBIM.App.UI.DesignSystem;

public static class TrueBimWindowChrome
{
    public static void Apply(Window window)
    {
        window.FontFamily = TrueBimTheme.FontFamily;
        window.FontSize = TrueBimTheme.FontSize;
        window.Background = TrueBimBrushes.WindowBackground;
        window.Foreground = TrueBimBrushes.TextPrimary;
        window.SnapsToDevicePixels = true;
        window.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);

        if (window.MinWidth <= 0)
        {
            window.MinWidth = 480;
        }

        if (window.MinHeight <= 0)
        {
            window.MinHeight = 320;
        }

        TrueBimStyles.RegisterLocalResources(window.Resources);
    }
}
