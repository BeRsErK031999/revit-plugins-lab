using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrueBIM.App.UI;

public enum TrueBimIcon
{
    App,
    Logs,
    Print,
    Visibility,
    SheetNumbering,
    ScheduleCollapse,
    VoltageDrop,
    IsoFieldRebar,
    ColorByParameter,
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
    Export,
    Apply,
    Close,
    Up,
    Down,
    Move
}

internal static class IconFactory
{
    private static readonly Dictionary<(TrueBimIcon Icon, double Size), ImageSource> ImageCache = [];

    public static ImageSource CreateImage(TrueBimIcon icon, double size = 16)
    {
        if (ImageCache.TryGetValue((icon, size), out ImageSource? cached))
        {
            return cached;
        }

        Geometry geometry = Geometry.Parse(GetGeometry(icon));
        Brush brush = icon == TrueBimIcon.App
            ? new SolidColorBrush(Color.FromRgb(30, 69, 148))
            : Brushes.DimGray;
        GeometryDrawing drawing = new(brush, null, geometry);
        DrawingImage image = new(drawing);
        image.Freeze();
        ImageCache[(icon, size)] = image;
        return image;
    }

    public static UIElement Create(TrueBimIcon icon, double size = 16)
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

    public static object CreateButtonContent(TrueBimIcon icon, string text)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(Create(icon));
        panel.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static string GetGeometry(TrueBimIcon icon)
    {
        return icon switch
        {
            TrueBimIcon.App => "M2,2 L14,2 L14,14 L2,14 Z M4,4 L12,4 L12,12 L4,12 Z M6,6 L10,6 L10,8 L6,8 Z M6,9 L11,9 L11,10.5 L6,10.5 Z",
            TrueBimIcon.Logs => "M3,2 L12,2 L12,14 L3,14 Z M5,5 L10,5 L10,6 L5,6 Z M5,8 L10,8 L10,9 L5,9 Z M5,11 L9,11 L9,12 L5,12 Z",
            TrueBimIcon.Print => "M4,1.5 L12,1.5 L12,5 L4,5 Z M3,5 L13,5 L14.5,7 L14.5,11.5 L12.5,11.5 L12.5,14.5 L3.5,14.5 L3.5,11.5 L1.5,11.5 L1.5,7 Z M4.5,10 L11.5,10 L11.5,13.5 L4.5,13.5 Z M4,7 L5.5,7 L5.5,8.2 L4,8.2 Z M6.5,11 L10,11 L10,12 L6.5,12 Z",
            TrueBimIcon.Visibility => "M1,8 C3,4.5 5.5,3 8,3 C10.5,3 13,4.5 15,8 C13,11.5 10.5,13 8,13 C5.5,13 3,11.5 1,8 Z M8,5 A3,3 0 1 0 8,11 A3,3 0 1 0 8,5 Z M8,6.5 A1.5,1.5 0 1 1 8,9.5 A1.5,1.5 0 1 1 8,6.5 Z M12.5,1.5 L14.5,3.5 L13.4,4.6 L11.4,2.6 Z",
            TrueBimIcon.SheetNumbering => "M3,1.5 L12,1.5 L12,14.5 L3,14.5 Z M5,4 L10,4 L10,5 L5,5 Z M5,7 L7,7 L7,8 L5,8 Z M8,7 L10,7 L10,8 L8,8 Z M5,10 L7,10 L7,11 L5,11 Z M8,10 L10,10 L10,11 L8,11 Z M12.5,5.5 L15,8 L12.5,10.5 L12.5,8.75 L10.5,8.75 L10.5,7.25 L12.5,7.25 Z",
            TrueBimIcon.ScheduleCollapse => "M1.5,2 L14.5,2 L14.5,13 L1.5,13 Z M2.5,4 L13.5,4 L13.5,5 L2.5,5 Z M2.5,6 L4,6 L4,12 L2.5,12 Z M5,6 L6.5,6 L6.5,12 L5,12 Z M7.5,6 L9,6 L9,12 L7.5,12 Z M10,6 L11.5,6 L11.5,12 L10,12 Z M12.5,6 L13.5,6 L13.5,12 L12.5,12 Z M4.4,8.3 L6.8,8.3 L6.8,7 L9.4,9.5 L6.8,12 L6.8,10.7 L4.4,10.7 Z",
            TrueBimIcon.VoltageDrop => "M2,2 L11,2 L14,5 L14,14 L2,14 Z M11,2 L11,5 L14,5 Z M4,5 L9,5 L9,6 L4,6 Z M4,8 L7,8 L6,10 L8,10 L5,14 L6,11 L4,11 Z M10,8 L12,8 L12,10 L10,10 Z M10,11 L12,11 L12,13 L10,13 Z",
            TrueBimIcon.IsoFieldRebar => "M2,12 L14,4 L14,5.8 L4.8,12 Z M2,8.8 L10.2,3 L10.2,4.8 L3.8,9.8 Z M3,3 L6.5,3 L6.5,4.2 L4.2,4.2 L4.2,6.5 L3,6.5 Z M7,7 L10,7 L10,8.2 L8.2,8.2 L8.2,10 L7,10 Z M10.5,10.5 L14,10.5 L14,11.7 L11.7,11.7 L11.7,14 L10.5,14 Z M3,13 L13,13 L13,14 L3,14 Z",
            TrueBimIcon.ColorByParameter => "M2,2 L14,2 L14,14 L2,14 Z M3.5,3.5 L6.5,3.5 L6.5,6.5 L3.5,6.5 Z M7.5,3.5 L12.5,3.5 L12.5,4.8 L7.5,4.8 Z M7.5,5.8 L11,5.8 L11,7.1 L7.5,7.1 Z M3.5,8 L6.5,8 L6.5,11 L3.5,11 Z M7.5,8 L12.5,8 L12.5,9.3 L7.5,9.3 Z M7.5,10.3 L10.8,10.3 L10.8,11.6 L7.5,11.6 Z M4,12.5 L6,12.5 L6,13.5 L4,13.5 Z M7,12.5 L9,12.5 L9,13.5 L7,13.5 Z M10,12.5 L12,12.5 L12,13.5 L10,13.5 Z",
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
            TrueBimIcon.Export => "M3,2 L10,2 L13,5 L13,14 L3,14 Z M10,2 L10,5 L13,5 Z M7.5,6 L7.5,10 L5.5,8 L4.5,9 L8,12 L11.5,9 L10.5,8 L8.5,10 L8.5,6 Z",
            TrueBimIcon.Apply => "M6.5,11.5 L2.5,7.5 L1.5,8.5 L6.5,13.5 L14.5,5.5 L13.5,4.5 Z",
            TrueBimIcon.Close => "M4,3 L8,7 L12,3 L13,4 L9,8 L13,12 L12,13 L8,9 L4,13 L3,12 L7,8 L3,4 Z",
            TrueBimIcon.Up => "M8,3 L13,9 L10,9 L10,13 L6,13 L6,9 L3,9 Z",
            TrueBimIcon.Down => "M6,3 L10,3 L10,7 L13,7 L8,13 L3,7 L6,7 Z",
            TrueBimIcon.Move => "M8,1 L11,4 L9,4 L9,7 L12,7 L12,5 L15,8 L12,11 L12,9 L9,9 L9,12 L11,12 L8,15 L5,12 L7,12 L7,9 L4,9 L4,11 L1,8 L4,5 L4,7 L7,7 L7,4 L5,4 Z",
            _ => "M2,2 L14,2 L14,14 L2,14 Z"
        };
    }
}
