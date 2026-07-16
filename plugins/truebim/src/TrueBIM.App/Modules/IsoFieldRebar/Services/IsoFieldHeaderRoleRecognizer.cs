using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

/// <summary>
/// Recognizes the four PK LIRA reinforcement layer markers in a raster header.
/// This deliberately narrow classifier avoids native OCR dependencies inside Revit.
/// </summary>
public sealed class IsoFieldHeaderRoleRecognizer
{
    private const byte DarkPixelThreshold = 180;
    private const int MaxHeaderHeight = 96;
    private const int MaxHeaderWidth = 4096;
    private const double MinimumScore = 0.82;
    private const double MinimumMargin = 0.06;

    private static readonly IReadOnlyList<RoleTemplate> Templates =
    [
        new(IsoFieldLayerRole.As1X,
        [
            "............................................",
            "............................................",
            "....##...##............##..##....####.......",
            "....#....##..........####...##..##..#.......",
            "...#....####...........##....#..#....#.....#",
            "...#....#..#...####....##.....##.....#.....#",
            "...#....#..#..##..#....##.....##.....#.....#",
            "...#...#...##..###.....##....####....#.....#",
            "...#...######....##....##....#..#....#.....#",
            "...#..##....####..##...##...#....#...#.....#",
            "...#..#.....##.####....##..##....##..#.....#",
            "....#...............................#.......",
            "....##.............................##......."
        ]),
        new(IsoFieldLayerRole.As2X,
        [
            "............................................",
            "............................................",
            "....##...##..........####..##....####.......",
            "....#....##.........##...#..##..##..#.......",
            "...#....####.............#...#..#....#.....#",
            "...#....#..#...####......#....##.....#.....#",
            "...#....#..#..##..#.....#.....##.....#.....#",
            "...#...#...##..###.....#.....####....#.....#",
            "...#...######....##...#......#..#....#.....#",
            "...#..##....####..##.#......#....#...#.....#",
            "...#..#.....##.####.######.##....##..#.....#",
            "....#...............................#.......",
            "....##.............................##......."
        ]),
        new(IsoFieldLayerRole.As3Y,
        [
            "............................................",
            "............................................",
            "....##...##..........####..##....####.......",
            "....#....##..........#..##..#....#..#.......",
            "...#....####.............#...#..#....#.....#",
            "...#....#..#...####.....##...####....#.....#",
            "...#....#..#..##..#....##.....##.....#.....#",
            "...#...#...##..###.......#....##.....#.....#",
            "...#...######....##......#....##.....#.....#",
            "...#..##....####..####...#....##.....#.....#",
            "...#..#.....##.####..####.....##.....#.....#",
            "....#...............................#.......",
            "....##.............................##......."
        ]),
        new(IsoFieldLayerRole.As4Y,
        [
            "............................................",
            "............................................",
            "....##...##.............#..##....####.......",
            "....#....##............##...#....#..#.......",
            "...#....####..........###....#..#....#.....#",
            "...#....#..#...####...#.#....####....#.....#",
            "...#....#..#..##..#..#..#.....##.....#.....#",
            "...#...#...##..###..##..#.....##.....#.....#",
            "...#...######....#########....##.....#.....#",
            "...#..##....####..##....#.....##.....#.....#",
            "...#..#.....##.####.....#.....##.....#.....#",
            "....#...............................#.......",
            "....##.............................##......."
        ])
    ];

    public IsoFieldHeaderRoleRecognition Recognize(BitmapSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        int headerHeight = Math.Min(MaxHeaderHeight, source.PixelHeight);
        if (source.PixelWidth < Templates[0].Width || headerHeight < Templates[0].Height)
        {
            return IsoFieldHeaderRoleRecognition.NotDetected;
        }

        int headerWidth = Math.Min(MaxHeaderWidth, source.PixelWidth);
        FormatConvertedBitmap converted = new(source, PixelFormats.Bgra32, null, 0);
        int stride = headerWidth * 4;
        byte[] pixels = new byte[stride * headerHeight];
        converted.CopyPixels(
            new Int32Rect(0, 0, headerWidth, headerHeight),
            pixels,
            stride,
            0);

        bool[] darkPixels = BuildDarkPixelMap(pixels, headerWidth, headerHeight, stride);
        int[] integral = BuildIntegralImage(darkPixels, headerWidth, headerHeight);
        TemplateScore[] scores = Templates
            .Select(template => new TemplateScore(
                template.Role,
                FindBestScore(template, darkPixels, integral, headerWidth, headerHeight)))
            .OrderByDescending(score => score.Score)
            .ToArray();

        TemplateScore best = scores[0];
        double margin = best.Score - scores[1].Score;
        if (best.Score < MinimumScore || margin < MinimumMargin)
        {
            return new IsoFieldHeaderRoleRecognition(null, best.Score, margin);
        }

        return new IsoFieldHeaderRoleRecognition(best.Role, best.Score, margin);
    }

    private static bool[] BuildDarkPixelMap(byte[] pixels, int width, int height, int stride)
    {
        bool[] darkPixels = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            int targetRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = sourceRow + (x * 4);
                int luminance = (pixels[sourceIndex]
                    + pixels[sourceIndex + 1]
                    + pixels[sourceIndex + 2]) / 3;
                darkPixels[targetRow + x] = luminance < DarkPixelThreshold
                    && pixels[sourceIndex + 3] > 0;
            }
        }

        return darkPixels;
    }

    private static int[] BuildIntegralImage(bool[] darkPixels, int width, int height)
    {
        int integralWidth = width + 1;
        int[] integral = new int[integralWidth * (height + 1)];
        for (int y = 0; y < height; y++)
        {
            int rowTotal = 0;
            for (int x = 0; x < width; x++)
            {
                if (darkPixels[(y * width) + x])
                {
                    rowTotal++;
                }

                integral[((y + 1) * integralWidth) + x + 1] =
                    integral[(y * integralWidth) + x + 1] + rowTotal;
            }
        }

        return integral;
    }

    private static double FindBestScore(
        RoleTemplate template,
        bool[] darkPixels,
        int[] integral,
        int width,
        int height)
    {
        double best = 0;
        int minSourcePixels = (int)Math.Floor(template.DarkPixelOffsets.Count * 0.65);
        int maxSourcePixels = (int)Math.Ceiling(template.DarkPixelOffsets.Count * 1.45);
        for (int y = 0; y <= height - template.Height; y++)
        {
            for (int x = 0; x <= width - template.Width; x++)
            {
                int sourcePixels = CountRectangle(integral, width, x, y, template.Width, template.Height);
                if (sourcePixels < minSourcePixels || sourcePixels > maxSourcePixels)
                {
                    continue;
                }

                int matches = 0;
                foreach ((int OffsetX, int OffsetY) offset in template.DarkPixelOffsets)
                {
                    if (darkPixels[((y + offset.OffsetY) * width) + x + offset.OffsetX])
                    {
                        matches++;
                    }
                }

                double score = (2.0 * matches) / (template.DarkPixelOffsets.Count + sourcePixels);
                if (score > best)
                {
                    best = score;
                }
            }
        }

        return best;
    }

    private static int CountRectangle(
        int[] integral,
        int sourceWidth,
        int x,
        int y,
        int width,
        int height)
    {
        int integralWidth = sourceWidth + 1;
        int left = x;
        int top = y;
        int right = x + width;
        int bottom = y + height;
        return integral[(bottom * integralWidth) + right]
            - integral[(top * integralWidth) + right]
            - integral[(bottom * integralWidth) + left]
            + integral[(top * integralWidth) + left];
    }

    private sealed class RoleTemplate
    {
        public RoleTemplate(IsoFieldLayerRole role, IReadOnlyList<string> rows)
        {
            Role = role;
            Height = rows.Count;
            Width = rows[0].Length;
            DarkPixelOffsets = rows
                .SelectMany((row, y) => row.Select((value, x) => (value, x, y)))
                .Where(item => item.value == '#')
                .Select(item => (item.x, item.y))
                .ToArray();
        }

        public IsoFieldLayerRole Role { get; }

        public int Width { get; }

        public int Height { get; }

        public IReadOnlyList<(int OffsetX, int OffsetY)> DarkPixelOffsets { get; }
    }

    private sealed record TemplateScore(IsoFieldLayerRole Role, double Score);
}
