using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ColorByParameter;

public sealed class ColorPaletteServiceTests
{
    [Fact]
    public void GenerateReturnsRequestedColorCount()
    {
        ColorPaletteService service = new();

        IReadOnlyList<ColorSwatch> colors = service.Generate(12);

        Assert.Equal(12, colors.Count);
        Assert.All(colors, color => Assert.Matches("^#[0-9A-F]{6}$", color.Hex));
    }

    [Fact]
    public void GenerateWithOffsetReturnsDifferentFirstColors()
    {
        ColorPaletteService service = new();

        IReadOnlyList<ColorSwatch> firstPass = service.Generate(4);
        IReadOnlyList<ColorSwatch> regenerated = service.Generate(4, paletteOffset: 1);

        Assert.Equal(4, regenerated.Count);
        Assert.NotEqual(firstPass.Select(color => color.Hex), regenerated.Select(color => color.Hex));
        Assert.Equal(firstPass[1].Hex, regenerated[0].Hex);
    }

    [Fact]
    public void GenerateReturnsEmptyListForZeroCount()
    {
        ColorPaletteService service = new();

        Assert.Empty(service.Generate(0));
    }

    [Fact]
    public void TryParseHexSupportsManualColorInput()
    {
        Assert.True(ColorSwatch.TryParseHex("#0A7BFF", out ColorSwatch color));
        Assert.Equal((byte)10, color.Red);
        Assert.Equal((byte)123, color.Green);
        Assert.Equal((byte)255, color.Blue);
        Assert.False(ColorSwatch.TryParseHex("wrong", out _));
    }
}
