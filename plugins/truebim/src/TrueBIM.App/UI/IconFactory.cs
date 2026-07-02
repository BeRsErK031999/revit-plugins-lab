using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrueBIM.App.UI;

public enum TrueBimIcon
{
    App,
    Logs,
    Print,
    SheetNumbering,
    ScheduleCollapse,
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
    public static ImageSource CreateImage(TrueBimIcon icon, double size = 16)
    {
        Geometry geometry = Geometry.Parse(GetGeometry(icon));
        Brush brush = icon == TrueBimIcon.App
            ? new SolidColorBrush(Color.FromRgb(30, 69, 148))
            : Brushes.DimGray;
        GeometryDrawing drawing = new(brush, null, geometry);
        DrawingImage image = new(drawing);
        image.Freeze();
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
            TrueBimIcon.SheetNumbering => "M3,1.5 L12,1.5 L12,14.5 L3,14.5 Z M5,4 L10,4 L10,5 L5,5 Z M5,7 L7,7 L7,8 L5,8 Z M8,7 L10,7 L10,8 L8,8 Z M5,10 L7,10 L7,11 L5,11 Z M8,10 L10,10 L10,11 L8,11 Z M12.5,5.5 L15,8 L12.5,10.5 L12.5,8.75 L10.5,8.75 L10.5,7.25 L12.5,7.25 Z",
            TrueBimIcon.ScheduleCollapse => "M1.5,2 L14.5,2 L14.5,13 L1.5,13 Z M2.5,4 L13.5,4 L13.5,5 L2.5,5 Z M2.5,6 L4,6 L4,12 L2.5,12 Z M5,6 L6.5,6 L6.5,12 L5,12 Z M7.5,6 L9,6 L9,12 L7.5,12 Z M10,6 L11.5,6 L11.5,12 L10,12 Z M12.5,6 L13.5,6 L13.5,12 L12.5,12 Z M4.4,8.3 L6.8,8.3 L6.8,7 L9.4,9.5 L6.8,12 L6.8,10.7 L4.4,10.7 Z",
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
