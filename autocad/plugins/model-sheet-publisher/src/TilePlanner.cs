using System;
using System.Collections.Generic;
namespace ModelSheetPublisher {
    public sealed class Tile
    {
        public Box Window;
        public double X, Y, Width, Height;
    }
    public static class TilePlanner
    {
        public static List<Tile> Plan(Sheet sheet, double mediaWidth, double mediaHeight)
        {
            sheet.Validate();
            if (mediaWidth <= 0 || mediaHeight <= 0) throw new ArgumentOutOfRangeException(nameof(mediaWidth));
            double s = sheet.PrintScale;
            double left = sheet.Center ? (sheet.PaperWidth - sheet.Window.Width * s) / 2 : sheet.Margin + sheet.OffsetX;
            double bottom = sheet.Center ? (sheet.PaperHeight - sheet.Window.Height * s) / 2 : sheet.Margin + sheet.OffsetY;
            double x0 = Math.Max(sheet.Margin, left), y0 = Math.Max(sheet.Margin, bottom);
            double x1 = Math.Min(sheet.PaperWidth - sheet.Margin, left + sheet.Window.Width * s);
            double y1 = Math.Min(sheet.PaperHeight - sheet.Margin, bottom + sheet.Window.Height * s);
            var result = new List<Tile>();
            for (double y = y0; y < y1 - 1e-7; y += mediaHeight)
            for (double x = x0; x < x1 - 1e-7; x += mediaWidth)
            {
                double w = Math.Min(mediaWidth, x1 - x), h = Math.Min(mediaHeight, y1 - y);
                result.Add(new Tile { X = x, Y = y, Width = w, Height = h,
                    Window = new Box(sheet.Window.X + (x - left) / s, sheet.Window.Y + (y - bottom) / s, w / s, h / s) });
            }
            if (result.Count == 0) throw new InvalidOperationException("Смещение полностью выводит область за границы бумаги.");
            return result;
        }
    }

}
