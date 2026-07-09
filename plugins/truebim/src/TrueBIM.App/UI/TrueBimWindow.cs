using System.Windows;
using System.Windows.Media;

namespace TrueBIM.App.UI;

public class TrueBimWindow : Window
{
    private static readonly FontFamily ModernFontFamily = new("Segoe UI Variable Text, Segoe UI");

    public TrueBimWindow()
    {
        FontFamily = ModernFontFamily;
        FontSize = 13;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
    }
}
