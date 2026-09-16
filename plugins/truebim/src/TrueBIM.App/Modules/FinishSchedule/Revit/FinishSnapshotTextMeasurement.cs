using System.Globalization;
using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal static class FinishSnapshotTextMeasurement
{
    private const double TextUnitsPerFoot = 12 * 96;
    private const double PaddingFeet = 2.0 / 304.8;

    public static double RequiredHeight(string text, TableCellStyle style, double widthFeet)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        System.Windows.Media.Typeface typeface = new(new System.Windows.Media.FontFamily(style.FontName),
            style.IsFontItalic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal,
            style.IsFontBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
            System.Windows.FontStretches.Normal);
        System.Windows.Media.FormattedText measured = new(normalized, CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, typeface, style.TextSize, System.Windows.Media.Brushes.Black, 1.0)
        {
            MaxTextWidth = Math.Max(style.TextSize * 2, (widthFeet - PaddingFeet) * TextUnitsPerFoot),
            Trimming = System.Windows.TextTrimming.None
        };
        // Body row heights returned by Revit omit automatic multiline expansion on a sheet.
        // Header snapshot cells have fixed heights, so reserve the measured wrapped text height.
        double explicitLines = normalized.Count(character => character == '\n') + 1;
        double height = Math.Max(measured.Height * 1.15, explicitLines * style.TextSize * 1.35);
        return height / TextUnitsPerFoot + PaddingFeet;
    }
}
