using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Services;

public sealed class ColorPaletteService
{
    private static readonly ColorSwatch[] BasePalette =
    [
        new(31, 119, 180),
        new(255, 127, 14),
        new(44, 160, 44),
        new(214, 39, 40),
        new(148, 103, 189),
        new(140, 86, 75),
        new(227, 119, 194),
        new(127, 127, 127),
        new(188, 189, 34),
        new(23, 190, 207),
        new(66, 135, 245),
        new(245, 88, 66),
        new(78, 180, 115),
        new(181, 105, 35),
        new(132, 82, 195),
        new(43, 150, 150),
        new(205, 70, 125),
        new(102, 120, 55),
        new(55, 95, 150),
        new(160, 60, 60)
    ];

    public IReadOnlyList<ColorSwatch> Generate(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        List<ColorSwatch> colors = new(count);
        for (int index = 0; index < count; index++)
        {
            ColorSwatch baseColor = BasePalette[index % BasePalette.Length];
            int cycle = index / BasePalette.Length;
            colors.Add(cycle == 0 ? baseColor : Shift(baseColor, cycle));
        }

        return colors;
    }

    private static ColorSwatch Shift(ColorSwatch color, int cycle)
    {
        double factor = cycle % 2 == 0 ? 0.78 : 1.18;
        byte red = Clamp(color.Red * factor);
        byte green = Clamp(color.Green * factor);
        byte blue = Clamp(color.Blue * factor);
        return new ColorSwatch(red, green, blue);
    }

    private static byte Clamp(double value)
    {
        return (byte)Math.Max(35, Math.Min(235, Math.Round(value)));
    }
}
