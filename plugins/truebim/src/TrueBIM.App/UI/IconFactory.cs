using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.UI;

public enum TrueBimIcon
{
    App,
    Logs,
    Print,
    Dwg,
    Pdf,
    Visibility,
    SheetNumbering,
    ScheduleCollapse,
    ScheduleImport,
    FinishSchedule,
    VoltageDrop,
    IsoFieldRebar,
    Lintels,
    ColorByParameter,
    Palette,
    CopyParameters,
    Parameters,
    Worksets,
    JoinCut,
    AutoDimensions,
    AutoTags,
    TitleBlock,
    DatumExtents,
    OpeningViews,
    ClashReport,
    FamilyManager,
    Help,
    Open,
    Preview,
    Refresh,
    Settings,
    Filter,
    Search,
    Check,
    Warning,
    Error,
    Info,
    Folder,
    Export,
    Import,
    Apply,
    Close,
    Family,
    Parameter,
    Cut,
    Up,
    Down,
    Move
}

internal static class IconFactory
{
    private static readonly Dictionary<(TrueBimIcon Icon, double Size), ImageSource> ImageCache = [];
    private static readonly Dictionary<(TrueBimIcon Icon, Color Color), ImageSource> ColoredImageCache = [];

    public static ImageSource CreateImage(TrueBimIcon icon, double size = TrueBimTheme.IconSizeSmall)
    {
        int pixelSize = NormalizePixelSize(size);
        if (ImageCache.TryGetValue((icon, pixelSize), out ImageSource? cached))
        {
            return cached;
        }

        Brush brush = icon == TrueBimIcon.App
            ? TrueBimBrushes.Primary
            : TrueBimBrushes.TextSecondary;
        ImageSource image = RenderPixelAlignedImage(icon, pixelSize, brush);
        ImageCache[(icon, pixelSize)] = image;
        return image;
    }

    public static ImageSource CreateImage(TrueBimIcon icon, Color color)
    {
        if (ColoredImageCache.TryGetValue((icon, color), out ImageSource? cached))
        {
            return cached;
        }

        Geometry geometry = Geometry.Parse(GetGeometry(icon));
        GeometryDrawing drawing = new(new SolidColorBrush(color), null, geometry);
        DrawingImage image = new(drawing);
        image.Freeze();
        ColoredImageCache[(icon, color)] = image;
        return image;
    }

    public static UIElement Create(TrueBimIcon icon, double size = TrueBimTheme.IconSizeSmall)
    {
        return new Image
        {
            Source = CreateImage(icon, size),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 6, 0)
        };
    }

    public static UIElement Create(TrueBimIcon icon, Color color, double size = TrueBimTheme.IconSizeSmall)
    {
        return new Image
        {
            Source = CreateImage(icon, color),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 6, 0)
        };
    }

    public static object CreateButtonContent(TrueBimIcon icon, string text)
    {
        return CreateButtonContent(icon, text, TrueBimTheme.TextSecondaryColor);
    }

    public static object CreateButtonContent(
        TrueBimIcon icon,
        string text,
        Color iconColor,
        double iconSize = TrueBimTheme.IconSizeSmall)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(Create(icon, iconColor, iconSize));
        panel.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static int NormalizePixelSize(double size)
    {
        if (double.IsNaN(size) || double.IsInfinity(size) || size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Icon size must be a positive finite value.");
        }

        return Math.Max(1, (int)Math.Round(size, MidpointRounding.AwayFromZero));
    }

    private static ImageSource RenderPixelAlignedImage(TrueBimIcon icon, int pixelSize, Brush brush)
    {
        Geometry geometry = Geometry.Parse(GetGeometry(icon));
        Rect bounds = geometry.Bounds;
        double padding = Math.Max(1, pixelSize * 0.06);
        double availableSize = Math.Max(1, pixelSize - (padding * 2));
        double scale = Math.Min(availableSize / bounds.Width, availableSize / bounds.Height);
        double offsetX = ((pixelSize - (bounds.Width * scale)) / 2) - (bounds.X * scale);
        double offsetY = ((pixelSize - (bounds.Height * scale)) / 2) - (bounds.Y * scale);

        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, pixelSize, pixelSize));
            context.PushTransform(new MatrixTransform(new Matrix(scale, 0, 0, scale, offsetX, offsetY)));
            context.DrawGeometry(brush, null, geometry);
            context.Pop();
        }

        RenderTargetBitmap bitmap = new(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static string GetGeometry(TrueBimIcon icon)
    {
        return icon switch
        {
            TrueBimIcon.App => "M2,2 L14,2 L14,14 L2,14 Z M4,4 L12,4 L12,12 L4,12 Z M6,6 L10,6 L10,8 L6,8 Z M6,9 L11,9 L11,10.5 L6,10.5 Z",
            TrueBimIcon.Logs => "M3,2 L12,2 L12,14 L3,14 Z M5,5 L10,5 L10,6 L5,6 Z M5,8 L10,8 L10,9 L5,9 Z M5,11 L9,11 L9,12 L5,12 Z",
            TrueBimIcon.Print => "M4,1.5 L12,1.5 L12,5 L4,5 Z M3,5 L13,5 L14.5,7 L14.5,11.5 L12.5,11.5 L12.5,14.5 L3.5,14.5 L3.5,11.5 L1.5,11.5 L1.5,7 Z M4.5,10 L11.5,10 L11.5,13.5 L4.5,13.5 Z M4,7 L5.5,7 L5.5,8.2 L4,8.2 Z M6.5,11 L10,11 L10,12 L6.5,12 Z",
            TrueBimIcon.Dwg => "M2,2 L10,2 L14,6 L14,14 L2,14 Z M10,2 L10,6 L14,6 Z M4,8 L6,8 C7,8 7.7,8.7 7.7,10 C7.7,11.3 7,12 6,12 L4,12 Z M5,9 L5,11 L5.9,11 C6.4,11 6.7,10.6 6.7,10 C6.7,9.4 6.4,9 5.9,9 Z M8.3,8 L9.2,11 L10,8 L10.9,8 L11.7,11 L12.6,8 L13.5,8 L12.2,12 L11.3,12 L10.5,9.3 L9.7,12 L8.8,12 L7.5,8 Z",
            TrueBimIcon.Pdf => "M2,2 L10,2 L14,6 L14,14 L2,14 Z M10,2 L10,6 L14,6 Z M4,8 L6.2,8 C7.2,8 7.8,8.6 7.8,9.5 C7.8,10.4 7.2,11 6.2,11 L5,11 L5,12 L4,12 Z M5,9 L5,10 L6.1,10 C6.5,10 6.8,9.8 6.8,9.5 C6.8,9.2 6.5,9 6.1,9 Z M8.4,8 L10,8 C11,8 11.7,8.8 11.7,10 C11.7,11.2 11,12 10,12 L8.4,12 Z M9.4,9 L9.4,11 L10,11 C10.4,11 10.7,10.6 10.7,10 C10.7,9.4 10.4,9 10,9 Z M12.2,8 L14,8 L14,9 L13.2,9 L13.2,9.7 L14,9.7 L14,10.7 L13.2,10.7 L13.2,12 L12.2,12 Z",
            TrueBimIcon.Visibility => "M1,8 C3,4.5 5.5,3 8,3 C10.5,3 13,4.5 15,8 C13,11.5 10.5,13 8,13 C5.5,13 3,11.5 1,8 Z M8,5 A3,3 0 1 0 8,11 A3,3 0 1 0 8,5 Z M8,6.5 A1.5,1.5 0 1 1 8,9.5 A1.5,1.5 0 1 1 8,6.5 Z M12.5,1.5 L14.5,3.5 L13.4,4.6 L11.4,2.6 Z",
            TrueBimIcon.SheetNumbering => "M3,1.5 L12,1.5 L12,14.5 L3,14.5 Z M5,4 L10,4 L10,5 L5,5 Z M5,7 L7,7 L7,8 L5,8 Z M8,7 L10,7 L10,8 L8,8 Z M5,10 L7,10 L7,11 L5,11 Z M8,10 L10,10 L10,11 L8,11 Z M12.5,5.5 L15,8 L12.5,10.5 L12.5,8.75 L10.5,8.75 L10.5,7.25 L12.5,7.25 Z",
            TrueBimIcon.ScheduleCollapse => "M1.5,2 L14.5,2 L14.5,13 L1.5,13 Z M2.5,4 L13.5,4 L13.5,5 L2.5,5 Z M2.5,6 L4,6 L4,12 L2.5,12 Z M5,6 L6.5,6 L6.5,12 L5,12 Z M7.5,6 L9,6 L9,12 L7.5,12 Z M10,6 L11.5,6 L11.5,12 L10,12 Z M12.5,6 L13.5,6 L13.5,12 L12.5,12 Z M4.4,8.3 L6.8,8.3 L6.8,7 L9.4,9.5 L6.8,12 L6.8,10.7 L4.4,10.7 Z",
            TrueBimIcon.ScheduleImport => "M1.5,2 L11,2 L14.5,5.5 L14.5,14 L1.5,14 Z M11,2 L11,5.5 L14.5,5.5 Z M3,5 L10,5 L10,6.2 L3,6.2 Z M3,7.5 L13,7.5 L13,8.5 L3,8.5 Z M3,10 L13,10 L13,11 L3,11 Z M5,4 L6,4 L6,13 L5,13 Z M9,4 L10,4 L10,13 L9,13 Z M11.4,10.8 L14.2,10.8 L14.2,9.4 L15.5,11.3 L14.2,13.2 L14.2,11.8 L11.4,11.8 Z",
            TrueBimIcon.FinishSchedule => "M2,2 L14,2 L14,14 L2,14 Z M3.5,4 L12.5,4 L12.5,5.2 L3.5,5.2 Z M3.5,7 L7,7 L7,8.2 L3.5,8.2 Z M8,7 L12.5,7 L12.5,8.2 L8,8.2 Z M3.5,10 L7,10 L7,11.2 L3.5,11.2 Z M8,10 L12.5,10 L12.5,11.2 L8,11.2 Z M6.6,1 L9.4,1 L9.4,3 L6.6,3 Z M11.2,11.5 L15,7.7 L16.3,9 L12.5,12.8 L10.8,13.2 Z",
            TrueBimIcon.VoltageDrop => "M2,2 L11,2 L14,5 L14,14 L2,14 Z M11,2 L11,5 L14,5 Z M4,5 L9,5 L9,6 L4,6 Z M4,8 L7,8 L6,10 L8,10 L5,14 L6,11 L4,11 Z M10,8 L12,8 L12,10 L10,10 Z M10,11 L12,11 L12,13 L10,13 Z",
            TrueBimIcon.IsoFieldRebar => "M2,12 L14,4 L14,5.8 L4.8,12 Z M2,8.8 L10.2,3 L10.2,4.8 L3.8,9.8 Z M3,3 L6.5,3 L6.5,4.2 L4.2,4.2 L4.2,6.5 L3,6.5 Z M7,7 L10,7 L10,8.2 L8.2,8.2 L8.2,10 L7,10 Z M10.5,10.5 L14,10.5 L14,11.7 L11.7,11.7 L11.7,14 L10.5,14 Z M3,13 L13,13 L13,14 L3,14 Z",
            TrueBimIcon.Lintels => "M1.5,3 L14.5,3 L14.5,5.2 L1.5,5.2 Z M3,5.2 L5,5.2 L5,13 L3,13 Z M11,5.2 L13,5.2 L13,13 L11,13 Z M5,11 L11,11 L11,13 L5,13 Z M5.8,6.5 L10.2,6.5 L10.2,7.8 L5.8,7.8 Z M6.6,8.4 L9.4,8.4 L9.4,9.7 L6.6,9.7 Z",
            TrueBimIcon.ColorByParameter => "M2,2 L14,2 L14,14 L2,14 Z M3.5,3.5 L6.5,3.5 L6.5,6.5 L3.5,6.5 Z M7.5,3.5 L12.5,3.5 L12.5,4.8 L7.5,4.8 Z M7.5,5.8 L11,5.8 L11,7.1 L7.5,7.1 Z M3.5,8 L6.5,8 L6.5,11 L3.5,11 Z M7.5,8 L12.5,8 L12.5,9.3 L7.5,9.3 Z M7.5,10.3 L10.8,10.3 L10.8,11.6 L7.5,11.6 Z M4,12.5 L6,12.5 L6,13.5 L4,13.5 Z M7,12.5 L9,12.5 L9,13.5 L7,13.5 Z M10,12.5 L12,12.5 L12,13.5 L10,13.5 Z",
            TrueBimIcon.Palette => "M8,1.5 C4.4,1.5 1.5,4.2 1.5,7.8 C1.5,11.5 4.4,14.5 8,14.5 C9.4,14.5 10.4,13.6 10.4,12.5 C10.4,11.6 9.7,10.9 8.8,10.9 L8,10.9 C7.2,10.9 6.6,10.3 6.6,9.5 C6.6,8.7 7.2,8.1 8,8.1 L11.9,8.1 C13.5,8.1 14.5,6.9 14.5,5.5 C14.5,3.2 11.7,1.5 8,1.5 Z M4.2,6 A1.1,1.1 0 1 0 4.2,3.8 A1.1,1.1 0 1 0 4.2,6 Z M7.5,4.5 A1.1,1.1 0 1 0 7.5,2.3 A1.1,1.1 0 1 0 7.5,4.5 Z M10.7,5.7 A1.1,1.1 0 1 0 10.7,3.5 A1.1,1.1 0 1 0 10.7,5.7 Z M4.3,10 A1.1,1.1 0 1 0 4.3,7.8 A1.1,1.1 0 1 0 4.3,10 Z",
            TrueBimIcon.CopyParameters => "M2,2 L9,2 L9,4 L4,4 L4,11 L2,11 Z M5,5 L12,5 L14,7 L14,14 L5,14 Z M12,5 L12,7 L14,7 Z M7,8 L11.5,8 L11.5,9 L7,9 Z M7,10.5 L12,10.5 L12,11.5 L7,11.5 Z M7,13 L10.5,13 L10.5,14 L7,14 Z",
            TrueBimIcon.Parameters => "M3,3 L7,3 L7,4.5 L14,4.5 L14,5.8 L7,5.8 L7,7.3 L3,7.3 L3,5.8 L1.5,5.8 L1.5,4.5 L3,4.5 Z M9,8.5 L13,8.5 L13,10 L14.5,10 L14.5,11.3 L13,11.3 L13,12.8 L9,12.8 L9,11.3 L1.5,11.3 L1.5,10 L9,10 Z",
            TrueBimIcon.Worksets => "M2,2 L7,2 L7,7 L2,7 Z M9,2 L14,2 L14,7 L9,7 Z M2,9 L7,9 L7,14 L2,14 Z M9,9 L14,9 L14,14 L9,14 Z M3.2,3.2 L5.8,3.2 L5.8,5.8 L3.2,5.8 Z M10.2,3.2 L12.8,3.2 L12.8,5.8 L10.2,5.8 Z M3.2,10.2 L5.8,10.2 L5.8,12.8 L3.2,12.8 Z M10.2,10.2 L12.8,10.2 L12.8,12.8 L10.2,12.8 Z",
            TrueBimIcon.JoinCut => "M2,2 L8,2 L8,8 L2,8 Z M3.2,3.2 L6.8,3.2 L6.8,6.8 L3.2,6.8 Z M8,8 L14,8 L14,14 L8,14 Z M9.2,9.2 L12.8,9.2 L12.8,12.8 L9.2,12.8 Z M9.8,2.8 L13.2,6.2 L12.2,7.2 L8.8,3.8 Z M3.2,12.2 L6.2,9.2 L7.2,10.2 L4.2,13.2 Z M6.2,9.2 L9.8,12.8 L8.8,13.8 L5.2,10.2 Z M9.8,3.8 L13.2,0.4 L14.2,1.4 L10.8,4.8 Z",
            TrueBimIcon.AutoDimensions => "M2,3 L14,3 L14,4.4 L2,4.4 Z M3,7 L13,7 L13,8.4 L3,8.4 Z M4,11 L12,11 L12,12.4 L4,12.4 Z M2,1.5 L3.2,1.5 L3.2,5.8 L2,5.8 Z M12.8,1.5 L14,1.5 L14,5.8 L12.8,5.8 Z M3,5.9 L5.2,7.7 L3,9.5 Z M13,5.9 L10.8,7.7 L13,9.5 Z M4,9.9 L6.2,11.7 L4,13.5 Z M12,9.9 L9.8,11.7 L12,13.5 Z",
            TrueBimIcon.AutoTags => "M2,2 L9.5,2 L14,6.5 L14,14 L2,14 Z M9.5,2 L9.5,6.5 L14,6.5 Z M4,5 L8,5 L8,6.2 L4,6.2 Z M4,8 L12,8 L12,9.2 L4,9.2 Z M4,11 L10,11 L10,12.2 L4,12.2 Z M11.2,10.8 L14.5,14.1 L13.6,15 L10.3,11.7 Z",
            TrueBimIcon.TitleBlock => "M2,2 L14,2 L14,14 L2,14 Z M3.2,3.2 L12.8,3.2 L12.8,9 L3.2,9 Z M3.2,10 L7,10 L7,12.8 L3.2,12.8 Z M8,10 L12.8,10 L12.8,11 L8,11 Z M8,12 L12.8,12 L12.8,12.8 L8,12.8 Z",
            TrueBimIcon.DatumExtents => "M2,7.2 L14,7.2 L14,8.8 L2,8.8 Z M7.2,2 L8.8,2 L8.8,14 L7.2,14 Z M3,3 L5,3 L5,5 L3,5 Z M11,3 L13,3 L13,5 L11,5 Z M3,11 L5,11 L5,13 L3,13 Z M11,11 L13,11 L13,13 L11,13 Z",
            TrueBimIcon.OpeningViews => "M2,13 L14,13 L14,14.2 L2,14.2 Z M3,3 L8,1.8 L13,3 L13,12 L3,12 Z M4.2,4 L7.3,3.3 L7.3,10.8 L4.2,10.8 Z M8.5,3.3 L11.8,4 L11.8,10.8 L8.5,10.8 Z M5,5 L6.5,5 L6.5,6.5 L5,6.5 Z M9.3,5 L11,5 L11,6.5 L9.3,6.5 Z",
            TrueBimIcon.ClashReport => "M2,2 L8,2 L8,8 L2,8 Z M8,8 L14,8 L14,14 L8,14 Z M3.2,3.2 L6.8,3.2 L6.8,6.8 L3.2,6.8 Z M9.2,9.2 L12.8,9.2 L12.8,12.8 L9.2,12.8 Z M10.8,1.6 L14.4,5.2 L13.2,6.4 L9.6,2.8 Z M1.6,10.8 L5.2,14.4 L6.4,13.2 L2.8,9.6 Z",
            TrueBimIcon.FamilyManager => "M2,3 L7,3 L8,5 L14,5 L14,13 L2,13 Z M3.2,6.2 L12.8,6.2 L12.8,11.8 L3.2,11.8 Z M5,7.5 L7,7.5 L7,9.5 L5,9.5 Z M8,7.5 L11,7.5 L11,8.5 L8,8.5 Z M8,9.5 L10.5,9.5 L10.5,10.5 L8,10.5 Z",
            TrueBimIcon.Help => "M7.2,10.9 L8.8,10.9 L8.8,12.4 L7.2,12.4 Z M5.3,5.9 C5.3,4.1 6.7,2.9 8.4,2.9 C10.2,2.9 11.4,4.1 11.4,5.7 C11.4,7 10.6,7.8 9.5,8.5 C8.8,8.9 8.6,9.2 8.6,10 L7.1,10 C7.1,8.8 7.6,8 8.5,7.4 C9.3,6.9 9.8,6.5 9.8,5.8 C9.8,5 9.2,4.5 8.4,4.5 C7.5,4.5 6.9,5.1 6.9,6 Z",
            TrueBimIcon.Open => "M2,4 L7,4 L8,6 L14,6 L14,13 L2,13 Z M3,7 L13,7 L12,12 L3,12 Z",
            TrueBimIcon.Preview => "M1,8 C3,4 6,3 8,3 C10,3 13,4 15,8 C13,12 10,13 8,13 C6,13 3,12 1,8 Z M8,5 A3,3 0 1 0 8,11 A3,3 0 1 0 8,5 Z M8,6.5 A1.5,1.5 0 1 1 8,9.5 A1.5,1.5 0 1 1 8,6.5 Z",
            TrueBimIcon.Refresh => "M3,7 C3.4,4.2 5.5,2.5 8.2,2.5 C10,2.5 11.5,3.3 12.5,4.6 L12.5,2.6 L14,2.6 L14,7 L9.6,7 L9.6,5.5 L11.3,5.5 C10.6,4.6 9.5,4 8.2,4 C6.2,4 4.8,5.2 4.5,7 Z M2,9 L6.4,9 L6.4,10.5 L4.7,10.5 C5.4,11.4 6.5,12 7.8,12 C9.8,12 11.2,10.8 11.5,9 L13,9 C12.6,11.8 10.5,13.5 7.8,13.5 C6,13.5 4.5,12.7 3.5,11.4 L3.5,13.4 L2,13.4 Z",
            TrueBimIcon.Settings => "M7,1.5 L9,1.5 L9.5,3.2 L11,3.8 L12.6,3 L14,4.4 L13.2,6 L13.8,7.5 L15.5,8 L15.5,10 L13.8,10.5 L13.2,12 L14,13.6 L12.6,15 L11,14.2 L9.5,14.8 L9,16.5 L7,16.5 L6.5,14.8 L5,14.2 L3.4,15 L2,13.6 L2.8,12 L2.2,10.5 L0.5,10 L0.5,8 L2.2,7.5 L2.8,6 L2,4.4 L3.4,3 L5,3.8 L6.5,3.2 Z M8,6 A3,3 0 1 0 8,12 A3,3 0 1 0 8,6 Z M8,7.5 A1.5,1.5 0 1 1 8,10.5 A1.5,1.5 0 1 1 8,7.5 Z",
            TrueBimIcon.Filter => "M1.5,3 L14.5,3 L9.5,8.6 L9.5,13 L6.5,14.5 L6.5,8.6 Z",
            TrueBimIcon.Search => "M7,2 A5,5 0 1 0 7,12 A5,5 0 1 0 7,2 Z M7,3.5 A3.5,3.5 0 1 1 7,10.5 A3.5,3.5 0 1 1 7,3.5 Z M10.6,11.6 L11.6,10.6 L15,14 L14,15 Z",
            TrueBimIcon.Check => "M6.5,11.5 L2.5,7.5 L1.5,8.5 L6.5,13.5 L14.5,5.5 L13.5,4.5 Z",
            TrueBimIcon.Warning => "M8,1.5 L15,14 L1,14 Z M7.2,5.2 L8.8,5.2 L8.6,10 L7.4,10 Z M7.2,11 L8.8,11 L8.8,12.5 L7.2,12.5 Z",
            TrueBimIcon.Error => "M8,1.5 A6.5,6.5 0 1 0 8,14.5 A6.5,6.5 0 1 0 8,1.5 Z M5,4 L8,7 L11,4 L12,5 L9,8 L12,11 L11,12 L8,9 L5,12 L4,11 L7,8 L4,5 Z",
            TrueBimIcon.Info => "M8,1.5 A6.5,6.5 0 1 0 8,14.5 A6.5,6.5 0 1 0 8,1.5 Z M7.2,6.5 L8.8,6.5 L8.8,12 L7.2,12 Z M7.2,4 L8.8,4 L8.8,5.5 L7.2,5.5 Z",
            TrueBimIcon.Folder => "M1.5,4 L6.5,4 L7.8,5.5 L14.5,5.5 L14.5,13 L1.5,13 Z M2.5,6.5 L13.5,6.5 L13,12 L3,12 Z",
            TrueBimIcon.Export => "M3,2 L10,2 L13,5 L13,14 L3,14 Z M10,2 L10,5 L13,5 Z M7.5,6 L7.5,10 L5.5,8 L4.5,9 L8,12 L11.5,9 L10.5,8 L8.5,10 L8.5,6 Z",
            TrueBimIcon.Import => "M3,2 L10,2 L13,5 L13,14 L3,14 Z M10,2 L10,5 L13,5 Z M8.5,6 L8.5,10 L10.5,8 L11.5,9 L8,12 L4.5,9 L5.5,8 L7.5,10 L7.5,6 Z",
            TrueBimIcon.Apply => "M6.5,11.5 L2.5,7.5 L1.5,8.5 L6.5,13.5 L14.5,5.5 L13.5,4.5 Z",
            TrueBimIcon.Close => "M4,3 L8,7 L12,3 L13,4 L9,8 L13,12 L12,13 L8,9 L4,13 L3,12 L7,8 L3,4 Z",
            TrueBimIcon.Family => "M2,3 L7,3 L8,5 L14,5 L14,13 L2,13 Z M3.2,6.2 L12.8,6.2 L12.8,11.8 L3.2,11.8 Z M5,7.5 L7,7.5 L7,9.5 L5,9.5 Z M8,7.5 L11,7.5 L11,8.5 L8,8.5 Z M8,9.5 L10.5,9.5 L10.5,10.5 L8,10.5 Z",
            TrueBimIcon.Parameter => "M3,3 L7,3 L7,4.5 L14,4.5 L14,5.8 L7,5.8 L7,7.3 L3,7.3 L3,5.8 L1.5,5.8 L1.5,4.5 L3,4.5 Z M9,8.5 L13,8.5 L13,10 L14.5,10 L14.5,11.3 L13,11.3 L13,12.8 L9,12.8 L9,11.3 L1.5,11.3 L1.5,10 L9,10 Z",
            TrueBimIcon.Cut => "M3,2 L14,13 L13,14 L2,3 Z M13,2 L14,3 L9.5,7.5 L8.5,6.5 Z M2,13 L6.5,8.5 L7.5,9.5 L3,14 Z M4,11 A2,2 0 1 0 4,15 A2,2 0 1 0 4,11 Z M12,1 A2,2 0 1 0 12,5 A2,2 0 1 0 12,1 Z",
            TrueBimIcon.Up => "M8,3 L13,9 L10,9 L10,13 L6,13 L6,9 L3,9 Z",
            TrueBimIcon.Down => "M6,3 L10,3 L10,7 L13,7 L8,13 L3,7 L6,7 Z",
            TrueBimIcon.Move => "M8,1 L11,4 L9,4 L9,7 L12,7 L12,5 L15,8 L12,11 L12,9 L9,9 L9,12 L11,12 L8,15 L5,12 L7,12 L7,9 L4,9 L4,11 L1,8 L4,5 L4,7 L7,7 L7,4 L5,4 Z",
            _ => "M2,2 L14,2 L14,14 L2,14 Z"
        };
    }
}
